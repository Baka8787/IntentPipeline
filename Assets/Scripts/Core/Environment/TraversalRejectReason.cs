namespace Project.Core.Environment
{
    public enum TraversalRejectReason
    {
        None = 0,
        NotGrounded,
        TooSlow,
        NoForwardHit,
        TooHigh,
        NoValidTop,
        TopTooSteep,
        DestinationBlocked,
        CorridorBlocked,
    }
}
