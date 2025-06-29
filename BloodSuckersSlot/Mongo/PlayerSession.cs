
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BloodSuckersSlot.Mongo
{

    public class PlayerSession
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement("playerId")]
        public string PlayerId { get; set; }

        [BsonElement("shopId")]
        public string ShopId { get; set; } // To link the player to the shop

        [BsonElement("balance")]
        public double Balance { get; set; }

        [BsonElement("freeSpinsRemaining")]
        public int FreeSpinsRemaining { get; set; }

        [BsonElement("totalWagered")]
        public double TotalWagered { get; set; }

        [BsonElement("totalWon")]
        public double TotalWon { get; set; }

        [BsonElement("lastActive")]
        public DateTime LastActive { get; set; } = DateTime.UtcNow;

        public int BetAmount { get; set; } = 25; // ✅ default if not overridden
    }

}
