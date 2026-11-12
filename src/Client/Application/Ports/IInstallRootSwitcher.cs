namespace LANPartyHub.Client.Application.Ports;

/// <summary>
/// Port of the PREPARATION and the SUSTITUTION of the installation root (AD-9, AC4,
/// FR-11).
///
/// <para>
/// El port existe porque la SUSTITUCIÓN de una instalación válida NO puede ser una
/// escritura encima de la anterior: si la versión nueva falla a medias, el árbol que
/// estaba instalado deja de ser jugable. Así que el árbol nuevo se PREPARA en una raíz
/// aparte, se CERTIFICA allí y sólo después SUSTITUYE al anterior, que hasta ese
/// momento sigue intacto (AC4, FR-11).
/// </para>
/// <para>
/// La raíz de PREPARACIÓN es HERMANA de la raíz activa, así que la sustitución es un
/// cambio de NOMBRE dentro del mismo volumen: no hay una ventana en la que la raíz no
/// exista (AD-9).
/// </para>
/// <para>
/// El port NO decide si la sustitución procede: eso es de la CERTIFICACIÓN, que es una
/// regla pura del dominio (AC5). El port sólo pone y quita carpetas (AD-1, AD-16).
/// </para>
/// </summary>
public interface IInstallRootSwitcher
{
    /// <summary>
    /// PREPARES a root of PREPARATION for a new tree, as a SIBLING of the active root,
    /// and returns its absolute path (AD-9, AC4, FR-11).
    ///
    /// <para>
    /// La raíz de preparación está VACÍA: el árbol nuevo se escribe desde cero, así que
    /// no puede heredar ni un archivo de la versión anterior (AC4).
    /// </para>
    /// </summary>
    /// <param name="activeRoot">Canonical absolute path of the active installation root.</param>
    /// <returns>The absolute path of the empty staging root.</returns>
    string PrepareStagingRoot(string activeRoot);

    /// <summary>
    /// SUSTITUYES the active root with the PREPARED one, and returns the root that was
    /// replaced (AD-9, AC4, FR-11).
    ///
    /// <para>
    /// La sustitución es un cambio de nombre: la raíz preparada pasa a ser la raíz
    /// activa y la anterior queda a un lado, lista para borrarse. El fallo de la
    /// sustitución DEJA la raíz anterior en su sitio, porque el cambio de nombre es la
    /// operación que no puede dejar el árbol a medias (AC4).
    /// </para>
    /// </summary>
    /// <param name="activeRoot">Canonical absolute path of the active installation root.</param>
    /// <param name="stagingRoot">
    /// Canonical absolute path of the PREPARED root, as <see cref="PrepareStagingRoot"/>
    /// returned it.
    /// </param>
    /// <returns>The absolute path of the root that was replaced.</returns>
    string CommitStagingRoot(string activeRoot, string stagingRoot);

    /// <summary>
    /// DISCARDS a PREPARED root and leaves the active one untouched (AD-9, AC4, FR-11).
    ///
    /// <para>
    /// El descarte NUNCA borra la raíz activa: es la garantía de que un fallo de la
    /// versión nueva no invalida la instalación anterior (AC4).
    /// </para>
    /// </summary>
    /// <param name="activeRoot">Canonical absolute path of the active installation root.</param>
    /// <param name="stagingRoot">Canonical absolute path of the PREPARED root.</param>
    void DiscardStagingRoot(string activeRoot, string stagingRoot);

    /// <summary>
    /// MOVES a tree from one root to another by RENAME, and returns whether it moved
    /// (FR-8, AD-9).
    ///
    /// <para>
    /// El árbol de un juego que quedó en la raíz COMPARTIDA se lleva a la raíz de SU
    /// juego: si no, el juego PARECE no instalado y el cliente lo vuelve a descargar
    /// entero. El cambio de nombre no puede dejar el árbol a medias, así que o se mueve
    /// entero o no se mueve (AC4, AD-9).
    /// </para>
    /// </summary>
    /// <param name="currentRoot">Path where the tree currently lives.</param>
    /// <param name="targetRoot">Path where the tree has to live.</param>
    /// <returns><c>true</c> when the tree moved.</returns>
    bool MoveRoot(string currentRoot, string targetRoot);
}
