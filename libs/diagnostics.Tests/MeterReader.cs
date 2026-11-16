using System.Diagnostics.Metrics;
using System.Globalization;

namespace Zenith.Diagnostics.Tests;

/// <summary>Reads every observable instrument of one meter once, the way a collector would.</summary>
internal static class MeterReader
{
    public static Dictionary<string, double> Collect(string meterName)
    {
        var seen = new Dictionary<string, double>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == meterName) l.EnableMeasurementEvents(instrument);
            }
        };

        void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) where T : struct =>
            seen[instrument.Name] = Convert.ToDouble(value, CultureInfo.InvariantCulture);

        listener.SetMeasurementEventCallback<long>(Record);
        listener.SetMeasurementEventCallback<double>(Record);
        listener.Start();
        listener.RecordObservableInstruments();
        return seen;
    }
}
