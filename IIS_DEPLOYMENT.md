# IIS Deployment Guide for BloodSuckersSlot

## Prerequisites
- Windows Server with IIS installed
- .NET 8.0 Runtime installed on the server
- MongoDB installed and configured

## Step 1: Prepare Your Files

1. **Copy the entire `publish` folder** to your server
2. **Extract the contents** to a folder like: `C:\inetpub\wwwroot\BloodSuckersSlot\`

## Step 2: Configure IIS

### Create Application Pool:
1. Open **IIS Manager**
2. Right-click **Application Pools** → **Add Application Pool**
3. Name: `BloodSuckersSlot`
4. .NET CLR Version: **No Managed Code** (for Blazor WebAssembly)
5. Managed Pipeline Mode: **Integrated**

### Create Website:
1. Right-click **Sites** → **Add Website**
2. Site name: `BloodSuckersSlot`
3. Physical path: `C:\inetpub\wwwroot\BloodSuckersSlot\`
4. Port: `80` (or your preferred port)
5. Application pool: `BloodSuckersSlot`

## Step 3: Configure URL Rewriting

1. **Install URL Rewrite Module** (if not already installed)
2. **Create `web.config`** in your site root with this content:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" arguments=".\BloodSuckersSlot.Api.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess" />
      <rewrite>
        <rules>
          <rule name="Handle History Mode and custom 404/500" stopProcessing="true">
            <match url="(.*)" />
            <conditions logicalGrouping="MatchAll">
              <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
              <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
            </conditions>
            <action type="Rewrite" url="/" />
          </rule>
        </rules>
      </rewrite>
      <staticContent>
        <mimeMap fileExtension=".wasm" mimeType="application/wasm" />
        <mimeMap fileExtension=".blat" mimeType="application/octet-stream" />
      </staticContent>
    </system.webServer>
  </location>
</configuration>
```

## Step 4: Deploy API Separately

Since your web app needs the API, you need to deploy the API as well:

1. **Publish the API:**
   ```bash
   cd BloodSuckersSlot.Api
   dotnet publish -c Release -o ./publish
   ```

2. **Create API Application in IIS:**
   - Create another application pool for the API
   - Create a sub-application under your main site
   - Path: `/api`
   - Physical path: `C:\inetpub\wwwroot\BloodSuckersSlot\api\`

## Step 5: Configure Environment Variables

Set these environment variables in IIS:
- `ASPNETCORE_ENVIRONMENT`: `Production`
- `MongoDB__ConnectionString`: Your MongoDB connection string

## Step 6: Test Your Deployment

1. **Start the website** in IIS Manager
2. **Browse to your site**: `http://your-server-ip/`
3. **Test API endpoints**: `http://your-server-ip/api/spin`

## Troubleshooting

### Common Issues:
1. **500 Error**: Check application pool settings
2. **404 Error**: Verify URL rewriting configuration
3. **API Not Found**: Ensure API application is properly configured
4. **MongoDB Connection**: Verify connection string and network access

### Check Logs:
- IIS logs: `C:\inetpub\logs\LogFiles\`
- Application logs: Check Event Viewer
- API logs: Check the logs folder in your API directory 