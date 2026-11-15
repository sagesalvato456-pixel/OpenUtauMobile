using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

namespace OpenLoidFlame.Browser.Services;

internal static partial class BrowserFileSystem
{
    [JSImport("flush", "OpenLoidFlameFileSystem")]
    internal static partial Task FlushAsync();
}
