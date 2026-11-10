# Keycloak realm

`realm-gridwise.json` is imported by the `keycloak` container when it starts (`start-dev --import-realm`). Keycloak runs in dev mode
with its embedded database, so the realm is created again from this file whenever the container is recreated; changes made in the
admin console are lost then. To keep a change, make it here.

Keycloak is served by the web container's nginx at http://localhost:5100/auth. Tokens are issued by
`http://localhost:5100/auth/realms/gridwise`.

| What | Value |
|---|---|
| Realm | `gridwise`, access tokens live 5 minutes |
| Realm roles | `admin`, `customer` (in the token as `realm_access.roles`) |
| Users | `admin` / `admin_dev_pw` (role admin), `customer` / `customer_dev_pw` (role customer); both also have `default-roles-gridwise`, which lets them use the account console (it adds `account` to `aud` and `offline_access`, `uma_authorization` to the roles) |
| `gridwise-web` | public client for the browser app: Authorization Code with PKCE (S256), redirect `http://localhost:5100/*` |
| `gridwise-cli` | public client with direct access grants (username and password in one request) |
| Audience | both clients put `gridwise-api` in the `aud` of the access token |
| Admin console | http://localhost:5100/auth/admin, user `KEYCLOAK_ADMIN_USER` / `KEYCLOAK_ADMIN_PASSWORD` from `.env.example` |

**Dev only.** The passwords above are for local development. `gridwise-cli` exists for scripts and smoke tests: direct access grants
send the user's password to the client and are deprecated in OAuth 2.1, so this client must not exist in a real environment, where
scripts would use a confidential client with client credentials instead.

A token for scripts:

```bash
curl -s http://localhost:5100/auth/realms/gridwise/protocol/openid-connect/token \
  -d grant_type=password -d client_id=gridwise-cli -d username=admin -d password=admin_dev_pw | jq -r .access_token
```
