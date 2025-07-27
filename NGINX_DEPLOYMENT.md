# Nginx Deployment Guide for BloodSuckersSlot

## Prerequisites
- Linux server (Ubuntu/CentOS)
- Nginx installed
- .NET 8.0 Runtime installed
- MongoDB installed and configured

## Step 1: Prepare Your Files

1. **Upload the `publish` folder** to your server
2. **Extract to**: `/var/www/bloodsuckersslot/`

```bash
# Create directory
sudo mkdir -p /var/www/bloodsuckersslot

# Upload and extract your files
sudo cp -r publish/* /var/www/bloodsuckersslot/
```

## Step 2: Configure Nginx

### Create Nginx Configuration:

```bash
sudo nano /etc/nginx/sites-available/bloodsuckersslot
```

Add this configuration:

```nginx
server {
    listen 80;
    server_name your-domain.com;  # Replace with your domain

    root /var/www/bloodsuckersslot/wwwroot;
    index index.html;

    # Serve static files
    location / {
        try_files $uri $uri/ /index.html;
    }

    # Handle Blazor WebAssembly files
    location ~* \.(wasm|blat)$ {
        add_header Content-Type application/octet-stream;
        add_header Cache-Control "public, max-age=31536000";
    }

    # Proxy API requests to your API server
    location /api/ {
        proxy_pass http://localhost:5000/;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # WebSocket support for SignalR
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
    }

    # Security headers
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-XSS-Protection "1; mode=block" always;
}
```

### Enable the Site:

```bash
# Create symlink
sudo ln -s /etc/nginx/sites-available/bloodsuckersslot /etc/nginx/sites-enabled/

# Test configuration
sudo nginx -t

# Reload Nginx
sudo systemctl reload nginx
```

## Step 3: Deploy API as Service

### Create API Service File:

```bash
sudo nano /etc/systemd/system/bloodsuckersslot-api.service
```

Add this content:

```ini
[Unit]
Description=BloodSuckersSlot API
After=network.target

[Service]
Type=exec
ExecStart=/usr/bin/dotnet /var/www/bloodsuckersslot-api/BloodSuckersSlot.Api.dll
WorkingDirectory=/var/www/bloodsuckersslot-api
User=www-data
Group=www-data
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000
Environment=MongoDB__ConnectionString=your-mongodb-connection-string

[Install]
WantedBy=multi-user.target
```

### Deploy API:

```bash
# Create API directory
sudo mkdir -p /var/www/bloodsuckersslot-api

# Upload API files (you need to publish the API separately)
sudo cp -r api-publish/* /var/www/bloodsuckersslot-api/

# Set permissions
sudo chown -R www-data:www-data /var/www/bloodsuckersslot-api

# Start the service
sudo systemctl daemon-reload
sudo systemctl enable bloodsuckersslot-api
sudo systemctl start bloodsuckersslot-api
```

## Step 4: Configure SSL (Optional but Recommended)

### Install Certbot:

```bash
sudo apt update
sudo apt install certbot python3-certbot-nginx
```

### Get SSL Certificate:

```bash
sudo certbot --nginx -d your-domain.com
```

## Step 5: Test Your Deployment

1. **Check API service status:**
   ```bash
   sudo systemctl status bloodsuckersslot-api
   ```

2. **Check Nginx status:**
   ```bash
   sudo systemctl status nginx
   ```

3. **Test your site:**
   - Web app: `http://your-domain.com/`
   - API: `http://your-domain.com/api/spin`

## Troubleshooting

### Check Logs:

```bash
# Nginx logs
sudo tail -f /var/log/nginx/error.log
sudo tail -f /var/log/nginx/access.log

# API logs
sudo journalctl -u bloodsuckersslot-api -f

# System logs
sudo journalctl -xe
```

### Common Issues:

1. **Permission Denied**: Check file permissions
   ```bash
   sudo chown -R www-data:www-data /var/www/bloodsuckersslot
   ```

2. **API Not Starting**: Check environment variables and MongoDB connection

3. **Nginx Configuration Error**: Test with `sudo nginx -t`

4. **Port Already in Use**: Check if port 5000 is available
   ```bash
   sudo netstat -tlnp | grep :5000
   ``` 