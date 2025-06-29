
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BloodSuckersSlot.Mongo
{
    public class GlobalConfig
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("rtpTarget")]
        public double RtpTarget { get; set; }

        [BsonElement("hitRateTarget")]
        public double HitRateTarget { get; set; }

        [BsonElement("formulaWeights")]
        public FormulaWeights FormulaWeights { get; set; }
    }

    public class FormulaWeights
    {
        [BsonElement("rtpWeight")]
        public double RtpWeight { get; set; }

        [BsonElement("hitRateWeight")]
        public double HitRateWeight { get; set; }

        [BsonElement("playerBalanceWeight")]
        public double PlayerBalanceWeight { get; set; }

        [BsonElement("engineProfitWeight")]
        public double EngineProfitWeight { get; set; }

        [BsonElement("volatilityWeight")]
        public double VolatilityWeight { get; set; }
    }

    public class EngineConfig
    {
        [BsonElement("slotRtpTarget")]
        public double SlotRtpTarget { get; set; }

        [BsonElement("slotHitRateTarget")]
        public double SlotHitRateTarget { get; set; }

        [BsonElement("slotMaxWinAmount")]
        public double SlotMaxWinAmount { get; set; }

        [BsonElement("noOfReelSetsToChooseFrom")]
        public int NoOfReelSetsToChooseFrom { get; set; }

        [BsonElement("formulaWeights")]
        public FormulaWeights FormulaWeights { get; set; }
    }

    public class ShopConfig
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("shopId")]
        public string ShopId { get; set; }

        [BsonElement("location")]
        public string Location { get; set; }

        [BsonElement("currency")]
        public string Currency { get; set; }

        [BsonElement("maxPayoutLimit")]
        public double MaxPayoutLimit { get; set; }

        [BsonElement("minBalanceThreshold")]
        public double MinBalanceThreshold { get; set; }

        [BsonElement("engineConfig")]
        public EngineConfig EngineConfig { get; set; }

        public double InitialBalance { get; set; } = 5000; // ✅ Default optional
    }

    public class PlayerConfig
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("playerId")]
        public string PlayerId { get; set; }

        [BsonElement("preferredCurrency")]
        public string PreferredCurrency { get; set; }

        [BsonElement("maxBetLimit")]
        public double MaxBetLimit { get; set; }

        [BsonElement("bonusEligible")]
        public bool BonusEligible { get; set; }

        [BsonElement("customEngineConfig")]
        public EngineConfig CustomEngineConfig { get; set; } // Optional

        public double Balance { get; set; } // ✅ Add this
    }




}
