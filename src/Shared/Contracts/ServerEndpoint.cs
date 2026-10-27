namespace LANPartyHub.Shared.Contracts;

/// <summary>
/// Address of the event server (AD-3). The mDNS discovery and the manual address are two
/// equivalent ways to reach the server and both converge on this contract.
/// </summary>
/// <param name="Address">Host name or IP address of the server.</param>
/// <param name="Port">TCP port of the HTTP API and the SignalR hub.</param>
public sealed record ServerEndpoint(string Address, int Port);
