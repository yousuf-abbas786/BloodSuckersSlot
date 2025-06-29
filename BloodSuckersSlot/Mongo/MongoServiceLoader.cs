

namespace BloodSuckersSlot.Mongo
{
    using MongoDB.Driver;
    using Microsoft.Extensions.Options;
    using MongoDB.Bson;

    public class MongoServiceLoader
    {
        private readonly IMongoDatabase _database;

        public MongoServiceLoader(IOptions<MongoDbSettings> settings)
        {
            var client = new MongoClient(settings.Value.ConnectionString);
            _database = client.GetDatabase(settings.Value.DatabaseName);

            EnsureIndexes(); // create indexes once on startup
        }

        public IMongoCollection<GlobalConfig> GlobalConfig => _database.GetCollection<GlobalConfig>("global_config");

        public IMongoCollection<ShopConfig> ShopConfigs => _database.GetCollection<ShopConfig>("shop_configs");

        public IMongoCollection<PlayerConfig> PlayerConfigs => _database.GetCollection<PlayerConfig>("player_configs");

        public IMongoCollection<ShopState> ShopStates => _database.GetCollection<ShopState>("shop_states");

        public IMongoCollection<PlayerSession> PlayerSessions => _database.GetCollection<PlayerSession>("player_sessions");

        private void EnsureIndexes()
        {
            ShopConfigs.Indexes.CreateOne(
                new CreateIndexModel<ShopConfig>(
                    Builders<ShopConfig>.IndexKeys.Ascending(s => s.ShopId),
                    new CreateIndexOptions { Unique = true }));

            PlayerConfigs.Indexes.CreateOne(
                new CreateIndexModel<PlayerConfig>(
                    Builders<PlayerConfig>.IndexKeys.Ascending(p => p.PlayerId),
                    new CreateIndexOptions { Unique = true }));

            ShopStates.Indexes.CreateOne(
                new CreateIndexModel<ShopState>(
                    Builders<ShopState>.IndexKeys.Ascending(s => s.ShopId),
                    new CreateIndexOptions { Unique = true }));

            PlayerSessions.Indexes.CreateOne(
                new CreateIndexModel<PlayerSession>(
                    Builders<PlayerSession>.IndexKeys
                        .Ascending(p => p.PlayerId)
                        .Ascending(p => p.ShopId),
                    new CreateIndexOptions { Unique = true }));
        }
    }

}
