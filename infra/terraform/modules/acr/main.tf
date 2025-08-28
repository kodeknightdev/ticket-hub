resource "azurerm_container_registry" "this" {
  name                          = var.name
  location                      = var.location
  resource_group_name           = var.resource_group_name
  sku                           = var.sku
  admin_enabled                 = false
  public_network_access_enabled = true
  tags                          = var.tags
}

# AKS pulls images with its kubelet identity; no registry passwords anywhere.
resource "azurerm_role_assignment" "acr_pull" {
  for_each                         = toset(var.pull_principal_ids)
  scope                            = azurerm_container_registry.this.id
  role_definition_name             = "AcrPull"
  principal_id                     = each.value
  skip_service_principal_aad_check = true
}
