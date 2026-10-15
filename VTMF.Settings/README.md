## Push to dockerhub

```bash
docker build --tag vtmf/settingsmanager:v2.16 --attest type=sbom,generator=docker/scout-sbom-indexer:latest --pull --push .
```
## Docker compose stack

```yaml
services:
  vtmf-settings:
    image: vtmf/settingsmanager:v2.16
    ports:
      - "8080:8080"
    env_file: settings.env
    environment:
      ASPNETCORE_ENVIRONMENT: Production
    volumes:
      - data-protection-keys:/app/keys

volumes:
  data-protection-keys:
```

### Environment variables in settings.env
```env
ENVVAR=Loaded
SQLCONNECTIONPARAMETERS__0__CLIENTNAME=DefaultMetaBase
SQLCONNECTIONPARAMETERS__0__SERVER=Server=tcp:demoterminal.vtmf.ee,7777;Initial Catalog=dev_iconfit;Persist Security Info=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Connection Timeout=30;
SQLCONNECTIONPARAMETERS__0__USERID=henryvool
SQLCONNECTIONPARAMETERS__0__PASSWORD=********

SQLCONNECTIONPARAMETERS__1__CLIENTNAME=prod-hydraulic
SQLCONNECTIONPARAMETERS__1__SERVER=Server=tcp:a320ladu.pilveserv.eu,9999;Initial Catalog=prod_hydraulic;Persist Security Info=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=True;Connection Timeout=30;
SQLCONNECTIONPARAMETERS__1__USERID=henryvool
SQLCONNECTIONPARAMETERS__1__PASSWORD=**********
```