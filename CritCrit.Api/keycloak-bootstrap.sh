#!/usr/bin/env bash
set -euo pipefail

# ── Keycloak Bootstrap Script for CritCrit ──
# This script bootstraps a fresh Keycloak instance with the realm, clients,
# roles, and users that CritCrit expects. Run this after Keycloak is healthy.
#
# Usage:
#   export KEYCLOAK_URL=http://localhost:8080
#   export KEYCLOAK_ADMIN_USER=admin
#   export KEYCLOAK_ADMIN_PASSWORD=admin
#   ./keycloak-bootstrap.sh
#
# The script is idempotent — safe to re-run.

KEYCLOAK_URL="${KEYCLOAK_URL:-http://localhost:8080}"
ADMIN_USER="${KEYCLOAK_ADMIN_USER:-admin}"
ADMIN_PASS="${KEYCLOAK_ADMIN_PASSWORD:-admin}"
REALM="api"

BASE="${KEYCLOAK_URL%/}"
MASTER_TOKEN_URL="$BASE/realms/master/protocol/openid-connect/token"
ADMIN_API="$BASE/admin/realms"

# ── 1. Obtain admin token ──
echo "[keycloak-bootstrap] Authenticating as Keycloak admin..."
ADMIN_TOKEN=$(curl -s -X POST "$MASTER_TOKEN_URL" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password" \
  -d "client_id=admin-cli" \
  -d "username=$ADMIN_USER" \
  -d "password=$ADMIN_PASS" | \
  python3 -c "import sys,json; print(json.load(sys.stdin).get('access_token',''))")

if [ -z "$ADMIN_TOKEN" ]; then
  echo "[keycloak-bootstrap] ERROR: Failed to get admin token. Is Keycloak running?"
  exit 1
fi

AUTH="Authorization: Bearer $ADMIN_TOKEN"

# ── 2. Create realm (api) ──
echo "[keycloak-bootstrap] Creating realm '$REALM'..."
curl -s -o /dev/null -w "%{http_code}" -X POST "$ADMIN_API" \
  -H "$AUTH" -H "Content-Type: application/json" \
  -d '{"realm":"api","enabled":true}' | grep -E "201|409" > /dev/null || true

# ── 2b. Configure SMTP on realm (MailPit, no auth) ──
SMTP_HOST="${KEYCLOAK_SMTP_HOST:-mailpit}"
SMTP_PORT="${KEYCLOAK_SMTP_PORT:-1025}"
SMTP_FROM="${KEYCLOAK_SMTP_FROM:-no-reply@critcrit.local}"
echo "[keycloak-bootstrap] Configuring realm SMTP (host=$SMTP_HOST port=$SMTP_PORT)..."
curl -s -o /dev/null -w "%{http_code}" -X PUT "$ADMIN_API/$REALM" \
  -H "$AUTH" -H "Content-Type: application/json" \
  -d "$(cat <<EOF
{
  "smtpServer": {
    "host": "$SMTP_HOST",
    "port": "$SMTP_PORT",
    "from": "$SMTP_FROM",
    "fromDisplayName": "CritCrit",
    "auth": "false",
    "ssl": "false",
    "starttls": "false"
  }
}
EOF
)" | grep -E "204|200" > /dev/null || true

# ── 3. Create realm role: critcrit.superadmin ──
echo "[keycloak-bootstrap] Creating realm role 'critcrit.superadmin'..."
curl -s -o /dev/null -w "%{http_code}" -X POST "$ADMIN_API/$REALM/roles" \
  -H "$AUTH" -H "Content-Type: application/json" \
  -d '{"name":"critcrit.superadmin","description":"CritCrit platform super administrator"}' | grep -E "201|409" > /dev/null || true

# ── 4. Create client: store.api (resource server / API audience) ──
echo "[keycloak-bootstrap] Creating client 'store.api'..."
curl -s -o /dev/null -w "%{http_code}" -X POST "$ADMIN_API/$REALM/clients" \
  -H "$AUTH" -H "Content-Type: application/json" \
  -d '{
    "clientId": "store.api",
    "name": "CritCrit API",
    "enabled": true,
    "protocol": "openid-connect",
    "publicClient": false,
    "serviceAccountsEnabled": true,
    "authorizationServicesEnabled": false,
    "directAccessGrantsEnabled": false,
    "standardFlowEnabled": true,
    "implicitFlowEnabled": false,
    "redirectUris": ["http://localhost:5000/*", "http://localhost:8080/*"],
    "webOrigins": ["+"]
  }' | grep -E "201|409" > /dev/null || true

# Add mappers (idempotent — 409 if already present)
CLIENT_UUID=$(curl -s "$ADMIN_API/$REALM/clients?clientId=store.api" -H "$AUTH" | \
  python3 -c "import sys,json; d=json.load(sys.stdin); print(d[0]['id'] if d else '')")
if [ -n "$CLIENT_UUID" ]; then
  echo "[keycloak-bootstrap] Ensuring audience mapper on 'store.api'..."
  curl -s -o /dev/null -w "%{http_code}" -X POST "$ADMIN_API/$REALM/clients/$CLIENT_UUID/protocol-mappers/models" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -d '{
      "name": "audience-store-api",
      "protocol": "openid-connect",
      "protocolMapper": "oidc-audience-mapper",
      "consentRequired": false,
      "config": {
        "included.client.audience": "store.api",
        "id.token.claim": "true",
        "access.token.claim": "true"
      }
    }' | grep -E "201|409" > /dev/null || true
  echo "[keycloak-bootstrap] Ensuring realm-roles mapper on 'store.api'..."
  curl -s -o /dev/null -w "%{http_code}" -X POST "$ADMIN_API/$REALM/clients/$CLIENT_UUID/protocol-mappers/models" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -d '{
      "name": "realm-roles",
      "protocol": "openid-connect",
      "protocolMapper": "oidc-usermodel-realm-role-mapper",
      "consentRequired": false,
      "config": {
        "multivalued": "true",
        "user.attribute": "foo",
        "id.token.claim": "true",
        "access.token.claim": "true",
        "claim.name": "realm_access.roles",
        "jsonType.label": "String"
      }
    }' | grep -E "201|409" > /dev/null || true
fi

# ── 5. Create client: store.api.swagger (Swagger UI / public client) ──
echo "[keycloak-bootstrap] Creating client 'store.api.swagger'..."
curl -s -o /dev/null -w "%{http_code}" -X POST "$ADMIN_API/$REALM/clients" \
  -H "$AUTH" -H "Content-Type: application/json" \
  -d '{
    "clientId": "store.api.swagger",
    "name": "CritCrit Swagger UI",
    "enabled": true,
    "protocol": "openid-connect",
    "publicClient": true,
    "standardFlowEnabled": true,
    "implicitFlowEnabled": false,
    "directAccessGrantsEnabled": true,
    "redirectUris": [
      "http://localhost:5000/*",
      "http://localhost:8080/*",
      "http://localhost:3000/*",
      "http://127.0.0.1:*"
    ],
    "webOrigins": ["+"]
  }' | grep -E "201|409" > /dev/null || true

SW_CLIENT_UUID=$(curl -s "$ADMIN_API/$REALM/clients?clientId=store.api.swagger" -H "$AUTH" | \
  python3 -c "import sys,json; d=json.load(sys.stdin); print(d[0]['id'] if d else '')")
if [ -n "$SW_CLIENT_UUID" ]; then
  echo "[keycloak-bootstrap] Ensuring audience mapper on 'store.api.swagger'..."
  curl -s -o /dev/null -w "%{http_code}" -X POST "$ADMIN_API/$REALM/clients/$SW_CLIENT_UUID/protocol-mappers/models" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -d '{
      "name": "audience-store-api",
      "protocol": "openid-connect",
      "protocolMapper": "oidc-audience-mapper",
      "consentRequired": false,
      "config": {
        "included.client.audience": "store.api",
        "id.token.claim": "true",
        "access.token.claim": "true"
      }
    }' | grep -E "201|409" > /dev/null || true
  echo "[keycloak-bootstrap] Ensuring realm-roles mapper on 'store.api.swagger'..."
  curl -s -o /dev/null -w "%{http_code}" -X POST "$ADMIN_API/$REALM/clients/$SW_CLIENT_UUID/protocol-mappers/models" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -d '{
      "name": "realm-roles",
      "protocol": "openid-connect",
      "protocolMapper": "oidc-usermodel-realm-role-mapper",
      "consentRequired": false,
      "config": {
        "multivalued": "true",
        "user.attribute": "foo",
        "id.token.claim": "true",
        "access.token.claim": "true",
        "claim.name": "realm_access.roles",
        "jsonType.label": "String"
      }
    }' | grep -E "201|409" > /dev/null || true
fi

# ── 5b. Create client: critcrit.web (Vue SPA / public client) ──
echo "[keycloak-bootstrap] Creating client 'critcrit.web'..."
curl -s -o /dev/null -w "%{http_code}" -X POST "$ADMIN_API/$REALM/clients" \
  -H "$AUTH" -H "Content-Type: application/json" \
  -d '{
    "clientId": "critcrit.web",
    "name": "CritCrit Web (Vue SPA)",
    "enabled": true,
    "protocol": "openid-connect",
    "publicClient": true,
    "standardFlowEnabled": true,
    "implicitFlowEnabled": false,
    "directAccessGrantsEnabled": false,
    "attributes": {
      "pkce.code.challenge.method": "S256",
      "post.logout.redirect.uris": "http://localhost:5173/*"
    },
    "redirectUris": [
      "http://localhost:5173/*",
      "http://127.0.0.1:5173/*"
    ],
    "webOrigins": [
      "http://localhost:5173",
      "http://127.0.0.1:5173"
    ]
  }' | grep -E "201|409" > /dev/null || true

WEB_CLIENT_UUID=$(curl -s "$ADMIN_API/$REALM/clients?clientId=critcrit.web" -H "$AUTH" | \
  python3 -c "import sys,json; d=json.load(sys.stdin); print(d[0]['id'] if d else '')")
if [ -n "$WEB_CLIENT_UUID" ]; then
  echo "[keycloak-bootstrap] Ensuring audience mapper on 'critcrit.web'..."
  curl -s -o /dev/null -w "%{http_code}" -X POST "$ADMIN_API/$REALM/clients/$WEB_CLIENT_UUID/protocol-mappers/models" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -d '{
      "name": "audience-store-api",
      "protocol": "openid-connect",
      "protocolMapper": "oidc-audience-mapper",
      "consentRequired": false,
      "config": {
        "included.client.audience": "store.api",
        "id.token.claim": "true",
        "access.token.claim": "true"
      }
    }' | grep -E "201|409" > /dev/null || true
  echo "[keycloak-bootstrap] Ensuring realm-roles mapper on 'critcrit.web'..."
  curl -s -o /dev/null -w "%{http_code}" -X POST "$ADMIN_API/$REALM/clients/$WEB_CLIENT_UUID/protocol-mappers/models" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -d '{
      "name": "realm-roles",
      "protocol": "openid-connect",
      "protocolMapper": "oidc-usermodel-realm-role-mapper",
      "consentRequired": false,
      "config": {
        "multivalued": "true",
        "user.attribute": "foo",
        "id.token.claim": "true",
        "access.token.claim": "true",
        "claim.name": "realm_access.roles",
        "jsonType.label": "String"
      }
    }' | grep -E "201|409" > /dev/null || true
fi

# ── 6. Create test users ──
echo "[keycloak-bootstrap] Creating test users..."

for USER_JSON in \
  '{"username":"superadmin","email":"superadmin@example.com","enabled":true,"emailVerified":true}' \
  '{"username":"admin","email":"admin@example.com","enabled":true,"emailVerified":true}' \
  '{"username":"owner","email":"owner@example.com","enabled":true,"emailVerified":true}' \
  '{"username":"member","email":"member@example.com","enabled":true,"emailVerified":true}' \
  '{"username":"viewer","email":"viewer@example.com","enabled":true,"emailVerified":true}' ; do

  USERNAME=$(echo "$USER_JSON" | python3 -c "import sys,json; print(json.load(sys.stdin)['username'])")
  curl -s -o /dev/null -w "%{http_code}" -X POST "$ADMIN_API/$REALM/users" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -d "$USER_JSON" | grep -E "201|409" > /dev/null || true

  # Get user id
  USER_ID=$(curl -s "$ADMIN_API/$REALM/users?username=$USERNAME&exact=true" -H "$AUTH" | \
    python3 -c "import sys,json; d=json.load(sys.stdin); print(d[0]['id'] if d else '')")

  if [ -n "$USER_ID" ]; then
    # Set password
    curl -s -o /dev/null -X PUT "$ADMIN_API/$REALM/users/$USER_ID/reset-password" \
      -H "$AUTH" -H "Content-Type: application/json" \
      -d '{"type":"password","value":"password","temporary":false}' || true
  fi
done

# ── 7. Assign critcrit.superadmin role to superadmin user ──
echo "[keycloak-bootstrap] Assigning critcrit.superadmin to user 'superadmin'..."
SA_USER_ID=$(curl -s "$ADMIN_API/$REALM/users?username=superadmin&exact=true" -H "$AUTH" | \
  python3 -c "import sys,json; d=json.load(sys.stdin); print(d[0]['id'] if d else '')")
if [ -n "$SA_USER_ID" ]; then
  # Get role representation
  ROLE_JSON=$(curl -s "$ADMIN_API/$REALM/roles/critcrit.superadmin" -H "$AUTH")
  curl -s -o /dev/null -X POST "$ADMIN_API/$REALM/users/$SA_USER_ID/role-mappings/realm" \
    -H "$AUTH" -H "Content-Type: application/json" \
    -d "[$ROLE_JSON]" || true
fi

echo "[keycloak-bootstrap] Done. Keycloak is configured for CritCrit."
echo ""
echo "  Realm:        $REALM"
echo "  API client:     store.api"
echo "  Swagger client: store.api.swagger"
echo "  Web client:     critcrit.web (Vue SPA @ http://localhost:5173)"
echo "  Users:        superadmin / admin / owner / member / viewer"
echo "  Password:     password"
echo "  SuperAdmin:   superadmin@example.com (role: critcrit.superadmin)"
echo ""
echo "  Next: update appsettings.Development.json with your Keycloak URL."
