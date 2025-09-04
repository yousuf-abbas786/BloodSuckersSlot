# BloodSuckers Slot API - Flexible Configuration System

## 🚀 Quick Start for Local Development

### Option 1: Using the provided scripts (Recommended)
```bash
# Windows (Command Prompt) - Start both API and Web
run-local-full.bat

# Windows (PowerShell) - Start both API and Web
.\run-local-full.ps1

# Windows (Command Prompt) - API only
run-local-dev.bat

# Windows (PowerShell) - API only
.\run-local-dev.ps1
```

### Option 2: Manual startup
```bash
# Terminal 1: Start API
cd BloodSuckersSlot.Api
dotnet run --environment Development

# Terminal 2: Start Web (in new terminal)
cd BloodSuckersSlot.Web
dotnet run
```

## 📋 Configuration Files

### Main Configuration (`appsettings.json`)
- **Production settings** with server MongoDB connection
- **Performance optimized** for high load
- **Swagger disabled** for security

### Development Configuration (`appsettings.Development.json`)
- **Server MongoDB** connection (same as production for full reel set access)
- **Debug logging** enabled
- **Swagger enabled** for API documentation
- **Reduced cache sizes** for development
- **Longer timeouts** for debugging

## 🔧 Configuration Sections

### Environment Settings
```json
{
  "Environment": "Development", // or "Production"
  "ApiSettings": {
    "EnableCors": true,
    "CorsOrigins": ["http://localhost:3000", "http://localhost:5001", "http://localhost:7178"],
    "RequestTimeoutSeconds": 60,
    "EnableSwagger": true,
    "EnableDetailedErrors": true
  }
}
```

### Performance Settings
```json
{
  "Performance": {
    "MaxCacheSize": 1000,           // Development: 1000, Production: 10000
    "MaxReelSetsPerRange": 100,     // Development: 100, Production: 1000
    "PrefetchRangeCount": 3,        // Development: 3, Production: 5
    "PrefetchRangeSize": 0.2,       // Development: 0.2, Production: 0.1
    "PrefetchIntervalSeconds": 60,  // Development: 60, Production: 30
    "SpinTimeoutSeconds": 30        // Development: 30, Production: 10
  }
}
```

### MongoDB Settings
```json
{
  "MongoDb": {
    "ConnectionString": "mongodb://mongoAdmin:O4BM0ng04232s3r0@37.27.59.180:27417/slots?authSource=admin", // Server (same as production)
    "Database": "slots",
    "ConnectionTimeout": 10,        // Development: 10, Production: 30
    "MaxPoolSize": 10               // Development: 10, Production: 100
  }
}
```

## 🌐 API Endpoints

### Development Mode
- **API Base**: `http://localhost:5000`
- **Swagger UI**: `http://localhost:5000/swagger`
- **Health Check**: `http://localhost:5000/health`
- **Configuration**: `http://localhost:5000/config` (Development only)

### Production Mode
- **API Base**: `http://0.0.0.0:5000`
- **Health Check**: `http://0.0.0.0:5000/health`
- **Swagger**: Disabled for security

## 🌐 Web Application

### Development Mode
- **Web Base**: `http://localhost:7178`
- **API Connection**: `http://localhost:5000` (local API)
- **Configuration**: Uses `appsettings.Development.json`

### Production Mode
- **Web Base**: `http://37.27.71.156:7178`
- **API Connection**: `http://37.27.71.156:5000` (server API)
- **Configuration**: Uses `appsettings.Production.json`

## 🗄️ MongoDB Setup

### Development Mode (Recommended)
- **Uses server MongoDB** - same connection as production
- **All reel sets available** - full access to your data
- **No local setup required** - just run the API

### Alternative: Local MongoDB (if needed)
1. Download MongoDB Community Server
2. Install and start MongoDB service
3. Update connection string in `appsettings.Development.json`
4. Default connection: `mongodb://localhost:27017/slots`

### Docker MongoDB (alternative)
```bash
# Start MongoDB container
docker run -d -p 27017:27017 --name mongodb mongo:latest

# Stop container
docker stop mongodb

# Remove container
docker rm mongodb
```

## 🔍 Debugging Features

### Development Mode Includes:
- ✅ **Detailed error messages**
- ✅ **Debug logging**
- ✅ **Swagger API documentation**
- ✅ **Configuration endpoint** (`/config`)
- ✅ **Health check endpoint** (`/health`)
- ✅ **Reduced cache sizes** for faster testing
- ✅ **Longer timeouts** for debugging
- ✅ **Local API connection** for web application

### Production Mode Includes:
- ✅ **Optimized performance**
- ✅ **Security features**
- ✅ **Minimal logging**
- ✅ **Health check endpoint** (`/health`)

## 🎯 Environment Switching

### Development
```bash
dotnet run --environment Development
# Uses: appsettings.Development.json
```

### Production
```bash
dotnet run --environment Production
# Uses: appsettings.json
```

### Custom Environment
```bash
dotnet run --environment Staging
# Uses: appsettings.Staging.json (if exists)
```

## 📊 Performance Differences

| Setting | Development | Production |
|---------|-------------|------------|
| Cache Size | 1,000 | 10,000 |
| Reel Sets per Range | 100 | 1,000 |
| Prefetch Ranges | 3 | 5 |
| Timeout | 30s | 10s |
| MongoDB Pool | 10 | 100 |
| Logging | Debug | Information |

## 🛠️ Troubleshooting

### CORS Issues
1. **Problem**: Web app can't connect to API
2. **Solution**: Ensure API CORS is configured for `http://localhost:7178`
3. **Check**: API logs for CORS errors

### MongoDB Connection Issues
1. Check if MongoDB is running: `netstat -an | findstr 27017`
2. Verify connection string in config
3. Check firewall settings
4. Try Docker MongoDB as alternative

### API Not Starting
1. Check port 5000 is available
2. Verify all dependencies are installed
3. Check configuration files for syntax errors
4. Review logs for specific errors

### Web App Not Starting
1. Check port 7178 is available
2. Ensure API is running first
3. Check web app configuration
4. Review browser console for errors

### Performance Issues
1. Adjust cache sizes in configuration
2. Monitor memory usage
3. Check MongoDB connection pool
4. Review timeout settings

## 📝 Notes

- **Lazy Loading**: Reel sets are loaded on-demand, not at startup
- **Memory Optimization**: Cache sizes are configurable per environment
- **Flexible Configuration**: Easy to switch between local and server environments
- **Debugging Support**: Development mode includes comprehensive debugging tools
- **Local Development**: Web app now connects to local API by default
