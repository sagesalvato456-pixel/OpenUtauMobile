using System.Threading.Tasks;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenLoidFlame.Services.Tracks;

public interface ITrackHeaderService
{
    Task<USinger?> PickSingerAsync(USinger? currentSinger = null);
    Task<Phonemizer?> PickPhonemizerAsync();
    Task<string?> PickRendererNameAsync(string[] supportedRenderers);
    Task<RendererSettingsSelection?> PickRendererAsync(UProject project, UTrack track);
    Task<string?> PickTrackNameAsync(string currentName);
    Task<string?> PickTrackColorAsync(string currentColorName);
    Task ShowTrackSettingsAsync(UTrack track);
}
