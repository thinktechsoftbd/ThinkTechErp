# ThinkTechErp API: configuration and Ubuntu deployment

## Local development (Windows or Linux)

Nothing to set up. When `ASPNETCORE_ENVIRONMENT=Development` (the Visual Studio and
`dotnet run` launch profiles):

- **JWT signing key**: generated on first start and kept per OS user outside the repo,
  in `%LOCALAPPDATA%\ThinkTechErp\jwt-development-signing-key` (Windows) or
  `~/.local/share/ThinkTechErp/jwt-development-signing-key` (Linux).
- **Connection strings**: read from `appsettings.Development.json`. `SmsApi` falls back
  to the `Sms` connection until a least-privilege login exists.

To use your own values instead, set them in Visual Studio with **Manage User Secrets**
on the API project, or as environment variables (`Authentication__Jwt__SigningKey`,
`ConnectionStrings__SmsApi`, ...). Those take precedence over the defaults above.

## Production (Ubuntu VPS)

In Production nothing is generated or defaulted: the API refuses to start unless the
signing key and all four connection strings (`Website`, `Sms`, `Security`, `SmsApi`)
are supplied. They come from either an environment file or systemd credentials.

### 1. Install the runtime

```bash
sudo apt update && sudo apt install -y aspnetcore-runtime-10.0
```

If the package isn't found, add the backports feed first:
`sudo add-apt-repository ppa:dotnet/backports`.

### 2. Publish and copy the app

On the development machine:

```bash
dotnet publish ThinkTechErpAPI/ThinkTechErpAPI -c Release -o publish/api
```

Copy `publish/api/` to `/opt/thinktecherp/api/` on the server, then:

```bash
sudo useradd --system --no-create-home --shell /usr/sbin/nologin thinktecherp
sudo chown -R root:thinktecherp /opt/thinktecherp/api
```

### 3. Provide the secrets

**Option 1, environment file** (simplest):

```bash
sudo mkdir -p /etc/thinktecherp
sudo cp deploy/linux/api.env.example /etc/thinktecherp/api.env
sudo nano /etc/thinktecherp/api.env      # fill in the key and connection strings
sudo chown root:root /etc/thinktecherp/api.env
sudo chmod 600 /etc/thinktecherp/api.env
```

**Option 2, systemd credentials** (secrets never appear in the process environment):
create one root-only file per secret, named after its configuration key, then enable
the `LoadCredential=` lines in `thinktecherp-api.service` and remove those keys from
`api.env`.

```bash
sudo mkdir -p /etc/thinktecherp/credentials
sudo chmod 700 /etc/thinktecherp/credentials
openssl rand -base64 48 | tr -d '\n' | sudo tee /etc/thinktecherp/credentials/Authentication__Jwt__SigningKey > /dev/null
printf '%s' 'Host=127.0.0.1;...;Search Path=security' | sudo tee /etc/thinktecherp/credentials/ConnectionStrings__Security > /dev/null
sudo chmod 600 /etc/thinktecherp/credentials/*
```

Write each file without a trailing newline, as above.

### 4. Install the service

```bash
sudo cp deploy/linux/thinktecherp-api.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now thinktecherp-api
sudo journalctl -u thinktecherp-api -f
```

### 5. Put nginx in front (TLS)

The service listens on `http://127.0.0.1:5000` only. The API trusts `X-Forwarded-For`
and `X-Forwarded-Proto` from loopback, so the rate limiter sees real client IPs.

```nginx
server {
    listen 80;
    server_name api.example.com;

    location / {
        proxy_pass         http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header   Host $host;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
    }
}
```

Then enable HTTPS with `sudo certbot --nginx -d api.example.com`.

### Rotating the signing key

Replace the key and restart: `sudo systemctl restart thinktecherp-api`. Every issued
token becomes invalid, so clients must sign in again.

### Integration clients

Server-side applications (e.g. a MediDesk installation syncing its organization) call
`api/security/integration/*` with a client ID and secret instead of a user's password.
They trade them at `POST api/security/integration/token` for a 10-minute token that only
the integration endpoints accept. Give each installation its own client:

1. Create a dedicated user for the client (e.g. "MediDesk – Mohonto Dental Care"),
   inactive and without roles. Its ID is recorded as `created_by`/`updated_by`, and the
   client may only change organizations created under it, so never use a person's account.
2. Generate the secret and its SHA-256 (the API keeps only the hash):

   ```bash
   SECRET=$(openssl rand -base64 48 | tr -d '\r\n')
   printf '%s' "$SECRET" | sha256sum | cut -d' ' -f1
   ```

3. Add the client to `api.env` (or one credential file per key) and restart:

   ```
   Authentication__Integration__Clients__medidesk_mohonto__SecretSha256=<hash>
   Authentication__Integration__Clients__medidesk_mohonto__ActingUserId=<user ID>
   Authentication__Integration__Clients__medidesk_mohonto__Scopes__0=organization.sync
   ```

4. Give the client ID and `$SECRET` to the installation only, over a secure channel.

To revoke a client, remove its keys and restart; its unexpired tokens stop working too.
To rotate, replace the hash, restart, and update the installation.
