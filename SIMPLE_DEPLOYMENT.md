# Simple Deployment Guide (Quick Test)

## For Quick Testing Only

This is the simplest way to test your deployment locally or on a basic server.

## Step 1: Start a Simple HTTP Server

### Option A: Using Python (if available)
```bash
# Navigate to your publish folder
cd publish

# Start Python HTTP server
python -m http.server 8080
```

### Option B: Using Node.js (if available)
```bash
# Install http-server globally
npm install -g http-server

# Navigate to your publish folder
cd publish

# Start server
http-server -p 8080
```

### Option C: Using .NET (Built-in)
```bash
# Navigate to your publish folder
cd publish

# Start .NET HTTP server
dotnet serve -p 8080
```

## Step 2: Deploy API Separately

You also need to deploy your API. Publish the API:

```bash
cd BloodSuckersSlot.Api
dotnet publish -c Release -o ./api-publish
```

Then run the API:
```bash
cd api-publish
dotnet BloodSuckersSlot.Api.dll --urls "http://localhost:5000"
```

## Step 3: Configure API URL

Since you're running the web app and API on different ports, update the configuration:

1. **Edit `wwwroot/appsettings.json`:**
   ```json
   {
     "ApiBaseUrl": "http://localhost:5000"
   }
   ```

2. **Or create a new file `wwwroot/appsettings.Development.json`:**
   ```json
   {
     "ApiBaseUrl": "http://localhost:5000"
   }
   ```

## Step 4: Test Your Application

1. **Start the API** (in one terminal):
   ```bash
   cd api-publish
   dotnet BloodSuckersSlot.Api.dll
   ```

2. **Start the web server** (in another terminal):
   ```bash
   cd publish
   python -m http.server 8080
   ```

3. **Access your application:**
   - Web app: `http://localhost:8080`
   - API: `http://localhost:5000/api/spin`

## Important Notes

⚠️ **This is for testing only!** For production:
- Use proper web servers (IIS, Nginx, Apache)
- Configure SSL/HTTPS
- Set up proper security
- Use environment variables for configuration

## Troubleshooting

1. **CORS Errors**: The API needs to allow your web app's origin
2. **API Not Found**: Make sure the API is running on port 5000
3. **MongoDB Connection**: Ensure MongoDB is running and accessible
4. **Port Conflicts**: Change ports if 8080 or 5000 are already in use 