using v2rayF.Models;

namespace v2rayF.ViewModels;

public sealed class RoutingModeOption
{
    public RoutingModeOption(RoutingMode mode, string label)
    {
        Mode = mode;
        Label = label;
    }

    public RoutingMode Mode { get; }

    public string Label { get; }

    public override string ToString() => Label;
}
