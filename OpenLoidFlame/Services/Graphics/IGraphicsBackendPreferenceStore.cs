using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenLoidFlame.Services.Graphics
{
    public interface IGraphicsBackendPreferenceStore
    {
        bool CanSave { get; }
        IReadOnlyList<string> Read(string platformId);
        Task SaveAsync(string platformId, IReadOnlyList<string> order);
    }
}
