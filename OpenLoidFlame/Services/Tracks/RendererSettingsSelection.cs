using OpenUtau.Core.Ustx;

namespace OpenLoidFlame.Services.Tracks;

/// <summary>渲染设置和表情图覆盖的联合草稿；null 跟随默认，空字符串关闭表情图。</summary>
public sealed record RendererSettingsSelection(URenderSettings Settings, string? ExpressionGraph);
