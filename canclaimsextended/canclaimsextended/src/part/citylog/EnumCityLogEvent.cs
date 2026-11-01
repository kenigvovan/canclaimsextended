namespace canclaimsextended.src.part.citylog
{
    /// <summary>What a city log entry is about. Stored by name, so members may be added anywhere.</summary>
    public enum EnumCityLogEvent
    {
        CityCreated,
        CitizenJoined,
        CitizenLeft,
        CitizenKicked,
        MayorChanged,
        PlotClaimed,
        PlotUnclaimed,
        CityRenamed,
        FeeChanged,
        PlotFeeUnpaid,
        ChunkClaimed,
        ChunkUnclaimed,
        PlotSold,
        PlotTypeChanged,
        PlotReshaped,
        RankGranted,
        RankRevoked
    }
}
