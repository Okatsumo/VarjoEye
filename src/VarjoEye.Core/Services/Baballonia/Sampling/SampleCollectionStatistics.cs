namespace VarjoEye.Core.Services.Baballonia.Sampling;

internal sealed class SampleCollectionStatistics
{
    public int Received { get; private set; }
    public int Accepted { get; private set; }
    public int MissingFeatures { get; private set; }
    public int InvalidTrackingStatus { get; private set; }
    public int TimestampMismatch { get; private set; }
    public int LowStability { get; private set; }
    public int RateLimited { get; private set; }
    public int CapacityReached { get; private set; }
    public int InvalidBlinkTarget { get; private set; }
    public int InvalidTimestamp { get; private set; }
    public int NonFiniteValues { get; private set; }

    public void RecordReceived() => Received++;

    public void RecordAccepted() => Accepted++;

    public void Record(SampleRejectionReason reason)
    {
        switch (reason)
        {
            case SampleRejectionReason.None:
                Accepted++;
                break;
            case SampleRejectionReason.MissingFeatures:
            case SampleRejectionReason.InvalidFeatureDimensions:
                MissingFeatures++;
                break;
            case SampleRejectionReason.NonFiniteValues:
                NonFiniteValues++;
                break;
            case SampleRejectionReason.InvalidTimestamp:
                InvalidTimestamp++;
                break;
            case SampleRejectionReason.MissingReferenceGaze:
            case SampleRejectionReason.TimestampMismatch:
                TimestampMismatch++;
                break;
            case SampleRejectionReason.InvalidTrackingStatus:
                InvalidTrackingStatus++;
                break;
            case SampleRejectionReason.LowStability:
                LowStability++;
                break;
            case SampleRejectionReason.RateLimited:
                RateLimited++;
                break;
            case SampleRejectionReason.CapacityReached:
                CapacityReached++;
                break;
            case SampleRejectionReason.InvalidBlinkTarget:
                InvalidBlinkTarget++;
                break;
        }
    }

    public void Reset()
    {
        Received = 0;
        Accepted = 0;
        MissingFeatures = 0;
        InvalidTrackingStatus = 0;
        TimestampMismatch = 0;
        LowStability = 0;
        RateLimited = 0;
        CapacityReached = 0;
        InvalidBlinkTarget = 0;
        InvalidTimestamp = 0;
        NonFiniteValues = 0;
    }
}
