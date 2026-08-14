using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PCMonitorUSB.Core;

public sealed record WakeOnLanInfo(
    bool Enabled,
    bool Available,
    string ComputerName,
    string MacAddress,
    string BroadcastAddress,
    int Port,
    string AdapterName,
    string Reason);

public static class WakeOnLanService
{
    public static WakeOnLanInfo Detect(bool enabled)
    {
        var computerName = Environment.MachineName;
        if (!enabled)
            return new(false, false, computerName, "", "", 9, "", "disabled");

        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsUsableEthernet)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && address.IPv4Mask is not null)
                .Select(address => new
                {
                    Network = network,
                    Address = address.Address,
                    Mask = address.IPv4Mask!,
                    HasGateway = network.GetIPProperties().GatewayAddresses.Any(gateway =>
                        gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any))
                }))
            .OrderByDescending(candidate => candidate.HasGateway)
            .ThenBy(candidate => candidate.Network.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selected = candidates.FirstOrDefault();
        if (selected is null)
            return new(true, false, computerName, "", "", 9, "", "no_ethernet");

        var mac = selected.Network.GetPhysicalAddress().GetAddressBytes();
        if (mac.Length != 6)
            return new(true, false, computerName, "", "", 9, selected.Network.Name, "invalid_mac");

        return new(
            true,
            true,
            computerName,
            string.Join(":", mac.Select(value => value.ToString("X2"))),
            CalculateBroadcast(selected.Address, selected.Mask).ToString(),
            9,
            selected.Network.Name,
            "ready");
    }

    public static IPAddress CalculateBroadcast(IPAddress address, IPAddress mask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        if (addressBytes.Length != 4 || maskBytes.Length != 4)
            throw new ArgumentException("Wake-on-LAN requires IPv4 addresses.");

        var broadcast = new byte[4];
        for (var index = 0; index < broadcast.Length; index++)
            broadcast[index] = (byte)(addressBytes[index] | ~maskBytes[index]);
        return new IPAddress(broadcast);
    }

    private static bool IsUsableEthernet(NetworkInterface network)
    {
        if (network.OperationalStatus != OperationalStatus.Up ||
            network.NetworkInterfaceType != NetworkInterfaceType.Ethernet ||
            network.GetPhysicalAddress().GetAddressBytes().Length != 6)
            return false;

        var description = (network.Name + " " + network.Description).ToLowerInvariant();
        string[] virtualMarkers = ["virtual", "hyper-v", "vmware", "vpn", "tap", "tunnel", "loopback", "bluetooth"];
        return !virtualMarkers.Any(description.Contains);
    }
}
