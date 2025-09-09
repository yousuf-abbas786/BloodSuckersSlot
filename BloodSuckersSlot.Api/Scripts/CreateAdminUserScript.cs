using MongoDB.Driver;
using BloodSuckersSlot.Shared.Models;
using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;

namespace BloodSuckersSlot.Api.Scripts
{
    public class CreateAdminUserScript
    {
        private readonly IMongoDatabase _database;

        public CreateAdminUserScript(IMongoDatabase database)
        {
            _database = database;
        }

        public async Task CreateAdminUserAsync()
        {
            var collection = _database.GetCollection<User>("users");
            
            // Check if admin user already exists
            var existingAdmin = await collection.Find(x => x.Username == "admin").FirstOrDefaultAsync();
            if (existingAdmin != null)
            {
                Console.WriteLine("Admin user already exists!");
                return;
            }

            // Create admin user
            var adminUser = new User
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Username = "admin",
                Email = "admin@bloodsuckersslot.com",
                PasswordHash = HashPassword("admin123"),
                Role = UserRole.ADMIN,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                FirstName = "System",
                LastName = "Administrator"
            };

            await collection.InsertOneAsync(adminUser);
            Console.WriteLine("Admin user created successfully!");
            Console.WriteLine("Username: admin");
            Console.WriteLine("Password: admin123");
        }

        public async Task CreateTestPlayerAsync()
        {
            var collection = _database.GetCollection<User>("users");
            
            // Check if test player already exists
            var existingPlayer = await collection.Find(x => x.Username == "player1").FirstOrDefaultAsync();
            if (existingPlayer != null)
            {
                Console.WriteLine("Test player already exists!");
                return;
            }

            // Create test player
            var player = new User
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Username = "player1",
                Email = "player1@bloodsuckersslot.com",
                PasswordHash = HashPassword("player123"),
                Role = UserRole.PLAYER,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                FirstName = "Test",
                LastName = "Player",
                PhoneNumber = "+1234567890",
                DateOfBirth = new DateTime(1990, 1, 1),
                Balance = 1000.00m,
                PlayerStatus = "Active",
                GroupIds = new List<string>() // Will be assigned to groups later
            };

            await collection.InsertOneAsync(player);
            Console.WriteLine("Test player created successfully!");
            Console.WriteLine("Username: player1");
            Console.WriteLine("Password: player123");
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }
    }
}
