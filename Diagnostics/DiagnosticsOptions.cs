namespace RentRight.Diagnostics;

public sealed class DiagnosticsOptions
{
    public bool Enabled { get; set; } = true;
    public double SuccessfulRequestLogSampleRate { get; set; } = 1;
}
