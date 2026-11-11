#!/usr/bin/env python3
"""Ejecuta una fase de Build Lite con Hermes y habla el contrato del driver.

Build Lite ejecuta cada fase de proveedor como un comando que escribe un stream
JSON de eventos estilo opencode en stdout. Ese formato lo leen dos sitios del
driver:

- ``build_loop.parse_skill_output`` arma el transcript legible a partir de los
  eventos ``text`` y ``tool``;
- ``build_lite.ImplementationProgress._transcript_milestone`` busca en los
  eventos de herramienta las señales de avance (un test que corrio, un comando
  que se lanzo).

Hermes emite su propio JSONL (``--format stream-json``): ``text``, ``tool_use``
y ``tool_result``. Este envoltorio traduce ese stream al contrato de opencode,
de modo que el driver no necesita saber que el proveedor es Hermes.

El prompt de la fase llega por stdin. Se usa en vez de un argumento para que no
haya que escaparlo ni se pierda en el historial del proceso.

Variables de entorno:

- ``BUILD_LITE_HERMES_BIN``      binario de Hermes (por defecto ``hermes``)
- ``BUILD_LITE_HERMES_MODEL``    modelo y proveedor (por defecto, el de la sesion)
- ``BUILD_LITE_HERMES_TOOLSETS`` toolsets separados por coma
- ``BUILD_LITE_HERMES_PROFILE``  perfil de Hermes
- ``BUILD_LITE_HERMES_ARGS``     argumentos extra (separados por espacio)
"""
from __future__ import annotations

import json
import os
import shlex
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent


def emit(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def text_event(text: str, session_id: str) -> None:
    emit({"type": "text", "sessionID": session_id, "part": {"type": "text", "text": text}})


def tool_event(
    tool: str,
    status: str,
    detail: str,
    session_id: str,
    command: str = "",
) -> None:
    """Emite un evento de herramienta con la forma que el driver ya lee.

    El estado lleva el comando en ``input.command`` porque es de donde la
    sonda de progreso lo extrae (``_tool_command``): sin ahi, una fase que
    ejecuta un test no produce milestone y el driver la daria por PARADA.
    El ``title`` lleva el detalle legible, que es de donde lo lee el
    transcript (``parse_skill_output``).
    """
    state: dict = {"status": status, "title": detail}
    if command:
        state["input"] = {"command": command}
    emit({
        "type": "tool",
        "sessionID": session_id,
        "part": {"type": "tool", "tool": tool, "state": state},
    })


def hermes_argv(prompt: str) -> list[str]:
    hermes = os.environ.get("BUILD_LITE_HERMES_BIN", "hermes")
    model = os.environ.get("BUILD_LITE_HERMES_MODEL", "").strip()
    toolsets = os.environ.get("BUILD_LITE_HERMES_TOOLSETS", "").strip()
    profile = os.environ.get("BUILD_LITE_HERMES_PROFILE", "").strip()
    extra = shlex.split(os.environ.get("BUILD_LITE_HERMES_ARGS", ""))

    argv = [hermes, "chat", "--format", "stream-json", "--yolo", "-Q"]
    if model:
        argv += ["-m", model]
    if toolsets:
        argv += ["-t", toolsets]
    if profile:
        argv += ["-p", profile]
    argv += extra
    argv += ["-q", prompt]
    return argv


def translate(raw: str, state: dict) -> None:
    """Traduce un evento de Hermes al contrato que el driver ya lee.

    ``state`` lleva el ``session_id`` y el mapa de las herramientas en uso,
    con su comando, porque el comando lo declara el evento de uso y lo
    consume el de resultado.
    """
    raw = raw.strip()
    if not raw.startswith("{"):
        return
    try:
        event = json.loads(raw)
    except json.JSONDecodeError:
        return

    session_id = state["session_id"]
    pending = state["pending"]

    kind = event.get("type", "")
    if kind == "system" and event.get("subtype") == "init":
        session_id = str(event.get("session_id") or "")
        state["session_id"] = session_id
        return
    if kind == "result":
        session_id = str(event.get("session_id") or session_id)
        state["session_id"] = session_id
        return
    if kind == "text":
        body = str(event.get("text") or "").strip()
        if body:
            text_event(body, session_id)
        return
    if kind == "tool_use":
        name = str(event.get("name") or "tool")
        call_id = str(event.get("tool_call_id") or name)
        payload = event.get("input")
        detail = ""
        command = ""
        if isinstance(payload, dict):
            for key in ("command", "path", "file_path", "description", "query"):
                if payload.get(key):
                    detail = str(payload[key])[:380]
                    break
            if not detail:
                detail = json.dumps(payload, ensure_ascii=False)[:380]
            # La sonda de progreso solo mira el comando, y solo acepta los
            # eventos constados como ``completed``: el comando se recuerda
            # aqui para poder llevarlo al evento de resultado.
            if isinstance(payload.get("command"), str):
                command = payload["command"]
        pending[call_id] = (name, command)
        tool_event(name, "running", detail, session_id, command)
        return
    if kind == "tool_result":
        call_id = str(event.get("tool_call_id") or "")
        known = pending.get(call_id)
        name = str(event.get("name") or (known[0] if known else "tool"))
        status = "error" if event.get("is_error") else "completed"
        output = str(event.get("output") or "")
        # El comando lo ejecuto la herramienta: el evento de uso lo declaro
        # como ``running``, y la sonda solo lee los ``completed``.
        command = ""
        if isinstance(event.get("input"), dict):
            if isinstance(event["input"].get("command"), str):
                command = event["input"]["command"]
        if not command and known:
            command = known[1]
        tool_event(name, status, output[:380], session_id, command)
        return


def main() -> int:
    provider = os.environ.get("BUILD_LITE_PROVIDER", "opencode").strip().lower()
    if provider != "hermes":
        sys.stderr.write(
            f"BUILD_LITE_PROVIDER={provider!r} no es 'hermes': este envoltorio solo "
            "sabe ejecutar fases con Hermes.\n")
        return 2

    prompt = sys.stdin.read()
    if not prompt.strip():
        sys.stderr.write("la fase llego sin prompt\n")
        return 2

    process = subprocess.Popen(
        hermes_argv(prompt),
        cwd=str(REPO_ROOT),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        stdin=subprocess.DEVNULL,
        text=True,
        bufsize=1,
    )

    state = {"session_id": "", "pending": {}}

    assert process.stdout is not None
    for raw in process.stdout:
        translate(raw, state)

    return process.wait()


if __name__ == "__main__":
    sys.exit(main())
