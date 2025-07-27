# Windows Server Deployment - Simple Steps

## Your Setup
- **Server**: Windows Server at `37.27.71.156`
- **API**: Already running on port `5000`
- **Web App**: To be deployed on port `8080`

## Step 1: Upload Files to Server

1. **Copy your `publish` folder** to the server
2. **Place it in a directory** like: `C:\BloodSuckersSlot\`

## Step 2: Install Python (if not already installed)

```cmd
# Download Python from https://www.python.org/downloads/
# Or use winget:
winget install Python.Python.3.11
```

## Step 3: Start the Web Application

### Option A: Manual Start
```cmd
cd C:\BloodSuckersSlot\publish
python -m http.server 8080
```

### Option B: Use the Batch Script
```cmd
# Run the provided start-web-app.bat file
start-web-app.bat
```

### Option C: Install as Windows Service (Recommended for Production)
```cmd
# Install NSSM first (download from https://nssm.cc/download)
# Then run:
install-web-service.bat

# Start the service
net start BloodSuckersSlotWeb
```

## Step 4: Configure Firewall

Allow port 8080 through Windows Firewall:

```cmd
netsh advfirewall firewall add rule name="BloodSuckersSlot Web" dir=in action=allow protocol=TCP localport=8080
```

## Step 5: Test Your Application

1. **Local test**: `http://localhost:8080`
2. **Remote test**: `http://37.27.71.156:8080`

## Step 6: Set Up Auto-Start (Optional)

### Method 1: Windows Service (Recommended)
```cmd
# Already handled by install-web-service.bat
```

### Method 2: Startup Folder
```cmd
# Copy start-web-app.bat to:
C:\Users\[YourUsername]\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\
```

## Troubleshooting

### Check if the app is running:
```cmd
netstat -an | findstr :8080
```

### Check logs:
- Look for Python/Node.js output in the console
- Check Windows Event Viewer for service errors

### Common Issues:
1. **Port 8080 in use**: Change port in the script
2. **Python not found**: Install Python or use Node.js
3. **Firewall blocking**: Configure Windows Firewall
4. **API connection failed**: Check if API is running on port 5000

## Quick Commands

```cmd
# Start web app
cd C:\BloodSuckersSlot\publish && python -m http.server 8080

# Check if running
netstat -an | findstr :8080

# Stop web app
# Press Ctrl+C in the console

# Check API connection
curl http://37.27.71.156:5000/api/spin
```

## Security Notes

⚠️ **For Production:**
- Use HTTPS (configure SSL certificate)
- Set up proper authentication
- Configure proper firewall rules
- Use environment variables for sensitive data
- Consider using IIS or Nginx for better security 