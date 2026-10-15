using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.Core.Tests.Printers;

public class PrinterDefinitionTests
{
    [Fact]
    public void ValuesAreTrimmed()
    {
        PrinterDefinition printer = new()
        {
            Id = " mk4 ",
            Name = "  Workshop MK4 ",
            ConnectionType = ConnectionType.PrusaLink,
            Address = " 192.168.1.20 ",
        };

        Assert.Equal("mk4", printer.Id);
        Assert.Equal("Workshop MK4", printer.Name);
        Assert.Equal("192.168.1.20", printer.Address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyRequiredValuesAreRejected(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => Create(id: value!));
        Assert.ThrowsAny<ArgumentException>(() => Create(name: value!));
        Assert.ThrowsAny<ArgumentException>(() => Create(address: value!));
    }

    [Fact]
    public void UndefinedConnectionTypeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(connectionType: (ConnectionType)0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(connectionType: (ConnectionType)42));
    }

    [Fact]
    public void WithExpressionKeepsInvariants()
    {
        PrinterDefinition printer = Create();

        Assert.ThrowsAny<ArgumentException>(() => printer with { Address = "" });
        Assert.Equal("Renamed", (printer with { Name = " Renamed " }).Name);
    }

    private static PrinterDefinition Create(
        string id = "mk4",
        string name = "MK4",
        ConnectionType connectionType = ConnectionType.PrusaLink,
        string address = "192.168.1.20") =>
        new() { Id = id, Name = name, ConnectionType = connectionType, Address = address };
}
