using System.Net;
using System.Runtime.InteropServices;

namespace Glimpse.Networking;

// Windows UPnP NAT COM API. No VPN, hosted discovery service or third-party executable.
public sealed class RouterPortMapping : IDisposable
{
    private readonly int _port;
    private readonly string _description;
    private int _disposed;
    public IPAddress ExternalAddress { get; }
    private RouterPortMapping(int port,string description,IPAddress external) { _port=port; _description=description; ExternalAddress=external; }
    public static async Task<RouterPortMapping> OpenAsync(IPAddress local,int port,CancellationToken token)
    {
        var operation=Task.Run(()=>Open(local,port));
        try { return await operation.WaitAsync(TimeSpan.FromSeconds(8),token); }
        catch {
            // A COM call cannot be cancelled. Remove a mapping if it completes after cancellation.
            _=operation.ContinueWith(task=>{ if(task.IsCompletedSuccessfully) task.Result.Dispose(); else _=task.Exception; },TaskScheduler.Default);
            throw;
        }
    }
    private static RouterPortMapping Open(IPAddress local,int port)
    {
        object? nat=null, collection=null, mapping=null;
        var description="Glimpse-"+Guid.NewGuid().ToString("N");
        bool added=false;
        try {
            var type=Type.GetTypeFromProgID("HNetCfg.NATUPnP") ?? throw new IOException("O recurso UPnP do Windows não está disponível.");
            nat=Activator.CreateInstance(type)!;
            collection=((dynamic)nat).StaticPortMappingCollection;
            if(collection is null) throw new IOException("O roteador não disponibilizou abertura automática de portas.");
            object? existing=null;
            try { existing=((dynamic)collection)[port,"TCP"]; } catch(COMException) { }
            if(existing is not null) { Release(existing); throw new IOException("Esta porta já está encaminhada no roteador. Escolha outra porta no Glimpse."); }
            mapping=((dynamic)collection).Add(port,"TCP",port,local.ToString(),true,description); added=true;
            var external=IPAddress.Parse((string)((dynamic)mapping).ExternalIPAddress);
            if(!IsPublicIpv4(external)) throw new IOException("O roteador recebeu um endereço privado. Pode haver outro roteador ou CGNAT da operadora. Use um IPv4 público acessível para hospedar.");
            return new(port,description,external);
        }
        catch { if(added && collection is not null) { try { ((dynamic)collection).Remove(port,"TCP"); } catch { } } throw; }
        finally { Release(mapping); Release(collection); Release(nat); }
    }
    public static bool IsPublicIpv4(IPAddress address)
    {
        var b=address.GetAddressBytes();
        return b.Length==4 && b[0] is >0 and <224 && b[0]!=10 && b[0]!=127 &&
            !(b[0]==100 && b[1] is >=64 and <=127) && !(b[0]==169 && b[1]==254) &&
            !(b[0]==172 && b[1] is >=16 and <=31) && !(b[0]==192 && b[1]==168) &&
            !(b[0]==198 && b[1] is 18 or 19) && !(b[0]==192 && b[1]==0 && b[2] is 0 or 2) &&
            !(b[0]==198 && b[1]==51 && b[2]==100) && !(b[0]==203 && b[1]==0 && b[2]==113);
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0) return;
        // Avoid blocking the UI during gateway discovery/cleanup.
        _=Task.Run(()=>{
            object? nat=null, collection=null, mapping=null;
            try {
                var type=Type.GetTypeFromProgID("HNetCfg.NATUPnP"); if(type is null) return;
                nat=Activator.CreateInstance(type)!; collection=((dynamic)nat).StaticPortMappingCollection;
                if(collection is null) return;
                mapping=((dynamic)collection)[_port,"TCP"];
                if(mapping is not null && (string)((dynamic)mapping).Description==_description) ((dynamic)collection).Remove(_port,"TCP");
            } catch { }
            finally { Release(mapping); Release(collection); Release(nat); }
        });
    }
    private static void Release(object? value) { if(value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
}
