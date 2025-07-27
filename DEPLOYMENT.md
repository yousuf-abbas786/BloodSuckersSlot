# BloodSuckersSlot Web Application - Server Deployment Guide

## Overview
This guide explains how to deploy the BloodSuckersSlot web application to a server environment.

## Architecture
- **Web App**: Blazor WebAssembly (client-side)
- **API**: ASP.NET Core Web API (server-side)
- **Database**: MongoDB (for reel sets)

## Deployment Options

### Option 1: Same Server Deployment (Recommended)
Deploy both web app and API on the same server under the same domain.

#### Steps:
1. **Build the Web Application:**
   ```bash
   cd BloodSuckersSlot.Web
   dotnet publish -c Release -o ./publish
   ```

2. **Build the API:**
   ```bash
   cd BloodSuckersSlot.Api
   dotnet publish -c Release -o ./publish
   ```

3. **Configure Web Server (IIS/Nginx/Apache):**
   - Serve the web app from the root path `/`
   - Serve the API from the path `/api/`
   - Configure URL rewriting to route API calls correctly

4. **Example Nginx Configuration:**
   ```nginx
   server {
       listen 80;
       server_name your-domain.com;
       
       # Serve Blazor WebAssembly app
       location / {
           root /path/to/BloodSuckersSlot.Web/publish/wwwroot;
           try_files $uri $uri/ /index.html;
       }
       
       # Proxy API requests
       location /api/ {
           proxy_pass http://localhost:5000/;
           proxy_set_header Host $host;
           proxy_set_header X-Real-IP $remote_addr;
           proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
           proxy_set_header X-Forwarded-Proto $scheme;
       }
   }
   ```

### Option 2: Separate Server Deployment
Deploy web app and API on different servers/domains.

#### Steps:
1. **Update Configuration:**
   - Modify `BloodSuckersSlot.Web/wwwroot/appsettings.Production.json`:
     ```json
     {
       "ApiBaseUrl": "https://your-api-server.com"
     }
     ```

2. **Update API CORS:**
   - Modify `BloodSuckersSlot.Api/Program.cs` to allow your web app domain:
     ```csharp
     policy.WithOrigins("https://your-web-app-domain.com")
     ```

3. **Deploy Separately:**
   - Deploy web app to your web server
   - Deploy API to your API server
   - Ensure both are accessible via HTTPS

## Environment Variables

### Web App Environment Variables:
- `ASPNETCORE_ENVIRONMENT`: Set to "Production"

### API Environment Variables:
- `ASPNETCORE_ENVIRONMENT`: Set to "Production"
- `MongoDB__ConnectionString`: Your MongoDB connection string

## Security Considerations

1. **HTTPS**: Always use HTTPS in production
2. **CORS**: Configure CORS properly for your domains
3. **API Keys**: Store sensitive configuration in environment variables
4. **Firewall**: Configure firewall rules appropriately

## Monitoring and Logging

1. **Application Logs**: Configure logging to appropriate destinations
2. **Performance Monitoring**: Set up monitoring for both web app and API
3. **Error Tracking**: Implement error tracking and alerting

## Troubleshooting

### Common Issues:
1. **CORS Errors**: Check CORS configuration in API
2. **API Not Found**: Verify API routing and proxy configuration
3. **SignalR Connection Issues**: Ensure WebSocket support is enabled
4. **MongoDB Connection**: Verify MongoDB connection string and network access

### Debug Steps:
1. Check browser console for JavaScript errors
2. Check API logs for server-side errors
3. Verify network connectivity between components
4. Test API endpoints directly using tools like Postman

## Performance Optimization

1. **Enable Compression**: Configure gzip/brotli compression
2. **Caching**: Implement appropriate caching strategies
3. **CDN**: Use CDN for static assets if needed
4. **Database Optimization**: Optimize MongoDB queries and indexes

## Backup and Recovery

1. **Database Backups**: Set up regular MongoDB backups
2. **Application Backups**: Backup application files and configuration
3. **Disaster Recovery**: Plan for server failure scenarios 