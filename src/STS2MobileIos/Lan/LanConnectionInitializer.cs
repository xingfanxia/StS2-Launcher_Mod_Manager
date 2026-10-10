using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Platform;
using System.Runtime.CompilerServices;

namespace STS2MobileIos.Lan;

internal sealed class LanConnectionInitializer(ulong id, LanAddress address) : IClientConnectionInitializer
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_connection")]
    private static extern ref ENetConnection Connection(ENetClient client);

    public async Task<NetErrorInfo?> Connect(NetClientGameService service, CancellationToken cancelToken = default)
    {
        var client = new ENetClient(service);
        service.Initialize(client, PlatformType.None);
        bool connected = false;
        try
        {
            var result = await client.ConnectToHost(id, address.Host, address.Port, cancelToken);
            connected = !result.HasValue && client.IsConnected;
            return result;
        }
        finally
        {
            // The game's cancellation path can exit before IsConnected becomes true,
            // in which case NetClientGameService.Disconnect does not close ENet.
            if (!connected && Connection(client) is { } connection)
            {
                connection.Destroy();
                connection.Dispose();
                Connection(client) = null;
            }
        }
    }
}
