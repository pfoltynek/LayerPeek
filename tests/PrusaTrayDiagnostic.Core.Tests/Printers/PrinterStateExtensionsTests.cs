using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.Core.Tests.Printers;

public class PrinterStateExtensionsTests
{
    public static TheoryData<PrinterState, bool, bool, bool> Classification => new()
    {
        // state, isReachable, hasActiveJob, requiresAttention
        { PrinterState.Unknown, false, false, false },
        { PrinterState.Idle, true, false, false },
        { PrinterState.Busy, true, false, false },
        { PrinterState.Printing, true, true, false },
        { PrinterState.Paused, true, true, false },
        { PrinterState.Finished, true, false, false },
        { PrinterState.Stopped, true, false, false },
        { PrinterState.Error, true, false, true },
        { PrinterState.Attention, true, false, true },
        { PrinterState.Offline, false, false, false },
        { PrinterState.Unauthorized, false, false, true },
    };

    [Theory]
    [MemberData(nameof(Classification))]
    public void ClassifiesEveryState(PrinterState state, bool isReachable, bool hasActiveJob, bool requiresAttention)
    {
        Assert.Equal(isReachable, state.IsReachable());
        Assert.Equal(hasActiveJob, state.HasActiveJob());
        Assert.Equal(requiresAttention, state.RequiresAttention());
    }

    [Fact]
    public void ClassificationCoversAllDefinedStates()
    {
        PrinterState[] covered = [.. Classification.Select(row => row.Data.Item1)];

        Assert.Equal(Enum.GetValues<PrinterState>().Order(), covered.Order());
    }

    [Fact]
    public void UndefinedValueIsTreatedAsUnknown()
    {
        PrinterState undefined = (PrinterState)999;

        Assert.False(undefined.IsReachable());
        Assert.False(undefined.HasActiveJob());
        Assert.False(undefined.RequiresAttention());
    }
}
