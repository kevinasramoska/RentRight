#!/usr/bin/env bash
# One-time setup for the free-tier Azure demo. Run it in Azure Cloud Shell (Bash), signed in as the
# subscription owner. It creates no billable resources: only a resource group, a GitHub deployment
# identity (OIDC, no client secret), its Contributor role on that group, and the SQL admin group.
# Safe to re-run: every step reuses what already exists. See docs/AZURE_DEPLOYMENT.md.
set -euo pipefail

REPOSITORY="${REPOSITORY:-kevinasramoska/RentRight}"
ENVIRONMENT="${ENVIRONMENT:-azure-demo}"
LOCATION="${LOCATION:-northeurope}"
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-rentright-demo}"
APP_NAME="${APP_NAME:-rentright-github-deploy}"
SQL_ADMIN_GROUP="${SQL_ADMIN_GROUP:-rentright-sql-admins}"

subscription_id="$(az account show --query id -o tsv)"
tenant_id="$(az account show --query tenantId -o tsv)"
echo "Subscription: $(az account show --query name -o tsv) ($subscription_id)"

for namespace in Microsoft.Web Microsoft.Sql Microsoft.ManagedIdentity; do
  az provider register --namespace "$namespace" --wait --output none
done

az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none
resource_group_id="$(az group show --name "$RESOURCE_GROUP" --query id -o tsv)"

client_id="$(az ad app list --display-name "$APP_NAME" --query '[0].appId' -o tsv)"
if [[ -z "$client_id" ]]; then
  client_id="$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)"
fi
principal_id="$(az ad sp list --filter "appId eq '$client_id'" --query '[0].id' -o tsv)"
if [[ -z "$principal_id" ]]; then
  principal_id="$(az ad sp create --id "$client_id" --query id -o tsv)"
fi

# GitHub may only exchange tokens for jobs that run in the protected deployment environment.
subject="repo:${REPOSITORY}:environment:${ENVIRONMENT}"
if [[ -z "$(az ad app federated-credential list --id "$client_id" --query "[?subject=='$subject'].name" -o tsv)" ]]; then
  az ad app federated-credential create --id "$client_id" --output none --parameters "{
    \"name\": \"github-${ENVIRONMENT}\",
    \"issuer\": \"https://token.actions.githubusercontent.com\",
    \"subject\": \"${subject}\",
    \"audiences\": [\"api://AzureADTokenExchange\"]
  }"
fi

if [[ -z "$(az role assignment list --assignee "$principal_id" --scope "$resource_group_id" --role Contributor --query '[0].id' -o tsv)" ]]; then
  az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal \
    --role Contributor --scope "$resource_group_id" --output none
fi

group_id="$(az ad group list --display-name "$SQL_ADMIN_GROUP" --query '[0].id' -o tsv)"
if [[ -z "$group_id" ]]; then
  group_id="$(az ad group create --display-name "$SQL_ADMIN_GROUP" --mail-nickname "$SQL_ADMIN_GROUP" --query id -o tsv)"
fi
owner_id="$(az ad signed-in-user show --query id -o tsv)"
for member in "$principal_id" "$owner_id"; do
  if [[ "$(az ad group member check --group "$group_id" --member-id "$member" --query value -o tsv)" != "true" ]]; then
    az ad group member add --group "$group_id" --member-id "$member"
  fi
done

cat <<EOF

Done. Add these as variables of the GitHub environment "${ENVIRONMENT}" (they are identifiers, not secrets):
  AZURE_CLIENT_ID=${client_id}
  AZURE_TENANT_ID=${tenant_id}
  AZURE_SUBSCRIPTION_ID=${subscription_id}
  AZURE_RESOURCE_GROUP=${RESOURCE_GROUP}
  AZURE_SQL_ADMIN_GROUP_NAME=${SQL_ADMIN_GROUP}
  AZURE_SQL_ADMIN_GROUP_ID=${group_id}
EOF
