resource "azurerm_key_vault" "this" {
  name                          = var.name
  location                      = var.location
  resource_group_name           = var.resource_group_name
  tenant_id                     = var.tenant_id
  sku_name                      = "standard"
  enable_rbac_authorization     = true
  purge_protection_enabled      = var.purge_protection_enabled
  soft_delete_retention_days    = 7
  public_network_access_enabled = true
  tags                          = var.tags
}

resource "azurerm_role_assignment" "deployer_admin" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = var.deployer_object_id
}

# Placeholders only (Stripe TEST keys); real values are set out of band and Terraform leaves them alone.
resource "azurerm_key_vault_secret" "placeholder" {
  for_each     = var.placeholder_secrets
  name         = each.key
  value        = each.value
  key_vault_id = azurerm_key_vault.this.id
  content_type = "placeholder"

  lifecycle {
    ignore_changes = [value, content_type]
  }

  depends_on = [azurerm_role_assignment.deployer_admin]
}
