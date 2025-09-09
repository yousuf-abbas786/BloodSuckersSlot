// Script to drop the old gamingentities collection
// Run this with: mongo your_database_name drop-old-collection.js

// Check if the old collection exists
if (db.gamingentities.count() > 0) {
    print("Found " + db.gamingentities.count() + " documents in old 'gamingentities' collection");
    
    // Show what's in the old collection
    print("Documents in old collection:");
    db.gamingentities.find().forEach(function(doc) {
        print("  - " + doc.username + " (" + doc.role + ")");
    });
    
    // Drop the old collection
    db.gamingentities.drop();
    print("Successfully dropped old 'gamingentities' collection");
} else {
    print("Old 'gamingentities' collection not found or already empty");
}

// Verify the correct collection still exists
print("Documents in correct 'gamingEntities' collection: " + db.gamingEntities.count());
