# Shared by the scripts that call the API (sourced, not run). Needs curl and jq.
#   source scripts/lib/auth.sh
#   TOKEN=$(get_token admin)        # or: get_token customer
#   curl -H "Authorization: Bearer $TOKEN" ...
# The token comes from Keycloak through the gridwise-cli client (password grant, dev only, see keycloak/README.md). It is valid for
# 30 minutes (a client-level setting of gridwise-cli), so a script can fetch it once at the start.
# Everything can be overridden in the environment:
#   KEYCLOAK_URL (default http://localhost:$WEB_PORT/auth, WEB_PORT default 5100), KEYCLOAK_REALM (gridwise),
#   GRIDWISE_ADMIN_USER / GRIDWISE_ADMIN_PASSWORD (admin / admin_dev_pw),
#   GRIDWISE_CUSTOMER_USER / GRIDWISE_CUSTOMER_PASSWORD (customer / customer_dev_pw).

get_token() {
  local role=${1:-} user password
  case "$role" in
    admin) user=${GRIDWISE_ADMIN_USER:-admin}; password=${GRIDWISE_ADMIN_PASSWORD:-admin_dev_pw} ;;
    customer) user=${GRIDWISE_CUSTOMER_USER:-customer}; password=${GRIDWISE_CUSTOMER_PASSWORD:-customer_dev_pw} ;;
    *) echo "get_token: unknown role '$role' (use admin or customer)" >&2; return 2 ;;
  esac

  local keycloak=${KEYCLOAK_URL:-http://localhost:${WEB_PORT:-5100}/auth}
  local url="$keycloak/realms/${KEYCLOAK_REALM:-gridwise}/protocol/openid-connect/token"
  local response
  if ! response=$(curl -sS --max-time 10 -w '\n%{http_code}' "$url" \
      -d grant_type=password -d client_id=gridwise-cli \
      --data-urlencode "username=$user" --data-urlencode "password=$password" 2>&1); then
    echo "get_token: Keycloak is not reachable at $keycloak (${response%%$'\n'*}). Is the stack running (docker compose up -d --wait)?" >&2
    return 1
  fi

  local code=${response##*$'\n'} body=${response%$'\n'*}
  if [[ "$code" != 200 ]]; then
    echo "get_token: Keycloak answered HTTP $code for user '$user' at $url: $body" >&2
    return 1
  fi
  jq -er .access_token <<< "$body" || { echo "get_token: no access_token in Keycloak's answer" >&2; return 1; }
}
