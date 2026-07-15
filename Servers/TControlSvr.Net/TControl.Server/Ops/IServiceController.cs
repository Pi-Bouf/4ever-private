using Microsoft.Extensions.Logging;
using TControl.Server.Control;

namespace TControl.Server.Ops;

/// <summary>
/// Abstraction over remote Windows service control. The C++ server used the Service Control Manager
/// (<c>StartService</c>/<c>ControlService</c>). The cross-platform default (<see cref="NoOpServiceController"/>)
/// cannot start/stop remote OS services, so these are logged no-ops returning failure; a Windows
/// implementation (System.ServiceProcess.ServiceController) can be dropped in behind this seam.
/// </summary>
public interface IServiceController
{
    bool Start(ServiceInstance svc);
    bool Stop(ServiceInstance svc);
}

/// <summary>Cross-platform default: logs the request and reports failure ("cannot control"). Service
/// liveness is instead derived from whether the control server has a live connection to the peer.</summary>
public sealed class NoOpServiceController : IServiceController
{
    private readonly ILogger<NoOpServiceController> _log;
    public NoOpServiceController(ILogger<NoOpServiceController> log) => _log = log;

    public bool Start(ServiceInstance svc)
    {
        _log.LogInformation("[no-op] Start service {Name} ({Id:X6}) — remote SCM control is disabled in this build.", svc.Name, svc.Id);
        return false;
    }

    public bool Stop(ServiceInstance svc)
    {
        _log.LogInformation("[no-op] Stop service {Name} ({Id:X6}) — remote SCM control is disabled in this build.", svc.Name, svc.Id);
        return false;
    }
}
