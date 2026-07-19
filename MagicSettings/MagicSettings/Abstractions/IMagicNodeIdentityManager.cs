using System.Text.Json.Nodes;
using MagicSettings.Share;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Globalization;
using System.ComponentModel.DataAnnotations;

namespace MagicSettings;

public interface IMagicNodeIdentityManager
{
    event EventHandler<MagicIdentityChange>? IdentityChanged;
    ValueTask<MagicNodeIdentityDescriptor> GetCurrentAsync(CancellationToken cancellationToken = default);
    ValueTask<MagicIdentityChange> RotateAsync(string reason, CancellationToken cancellationToken = default);
    ValueTask<MagicIdentityChange> ResetAsync(MagicIdentityResetRequest request, CancellationToken cancellationToken = default);
}
