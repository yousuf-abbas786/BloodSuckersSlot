
namespace BloodSuckersSlot.Mongo
{
    using MongoDB.Bson;
    using MongoDB.Bson.Serialization.Attributes;

    public class ShopState
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("shopId")]
        public string ShopId { get; set; }

        [BsonElement("currency")]
        public string Currency { get; set; }

        [BsonElement("currentBalance")]
        public double CurrentBalance { get; set; }

        [BsonElement("totalWagered")]
        public double TotalWagered { get; set; }

        [BsonElement("totalPayout")]
        public double TotalPayout { get; set; }

        [BsonIgnore]
        public double Profit => TotalWagered - TotalPayout;

        [BsonElement("lastUpdated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        public double Balance { get; set; } // ✅ Add this
    }

}
