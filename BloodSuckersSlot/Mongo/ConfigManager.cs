

using MongoDB.Driver;

namespace BloodSuckersSlot.Mongo
{
    public class ConfigManager
    {
        private readonly MongoServiceLoader _mongo;

        public ConfigManager(MongoServiceLoader mongo)
        {
            _mongo = mongo;
        }

        public async Task<GlobalConfig> GetGlobalConfigAsync()
        {
            return await _mongo.GlobalConfig.Find(_ => true).FirstOrDefaultAsync();
        }

        public async Task<ShopConfig> GetShopConfigAsync(string shopId)
        {
            return await _mongo.ShopConfigs.Find(s => s.ShopId == shopId).FirstOrDefaultAsync();
        }

        public async Task<PlayerConfig> GetPlayerConfigAsync(string playerId)
        {
            return await _mongo.PlayerConfigs.Find(p => p.PlayerId == playerId).FirstOrDefaultAsync();
        }
    }

}
