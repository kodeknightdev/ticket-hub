locals {
  env    = "prod"
  prefix = "tr-${local.env}-${var.location_short}"
  tags = merge({
    project     = "ticket-hub"
    environment = local.env
    managed_by  = "terraform"
  }, var.tags)
}

resource "azurerm_resource_group" "this" {
  name     = "${local.prefix}-rg"
  location = var.location
  tags     = local.tags
}

module "log_analytics" {
  source              = "../../modules/log-analytics"
  name                = "${local.prefix}-law"
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  retention_days      = 90
  tags                = local.tags
}

module "aks" {
  source                     = "../../modules/aks"
  name                       = "${local.prefix}-aks"
  location                   = var.location
  resource_group_name        = azurerm_resource_group.this.name
  sku_tier                   = "Standard"
  system_node_count          = 3
  user_vm_size               = "Standard_D4s_v5"
  user_min_count             = 3
  user_max_count             = 12
  admin_group_object_ids     = var.aks_admin_group_object_ids
  log_analytics_workspace_id = module.log_analytics.id
  tags                       = local.tags
}

module "acr" {
  source              = "../../modules/acr"
  name                = "th${local.env}${var.location_short}acr"
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  sku                 = "Premium"
  pull_principal_ids  = [module.aks.kubelet_object_id]
  tags                = local.tags
}

module "mssql" {
  source                        = "../../modules/mssql"
  name                          = "${local.prefix}-sql"
  location                      = var.location
  resource_group_name           = azurerm_resource_group.this.name
  entra_admin_login             = var.sql_admin_group_name
  entra_admin_object_id         = var.sql_admin_group_object_id
  public_network_access_enabled = false
  analytics_sku                 = "S4"
  analytics_max_size_gb         = 250
  private_endpoint_subnet_id    = var.private_endpoint_subnet_id
  tags                          = local.tags
}

module "redis" {
  source                        = "../../modules/redis"
  name                          = "${local.prefix}-redis"
  location                      = var.location
  resource_group_name           = azurerm_resource_group.this.name
  sku_name                      = "Standard"
  capacity                      = 2
  public_network_access_enabled = false
  tags                          = local.tags
}

module "keyvault" {
  source              = "../../modules/keyvault"
  name                = "${local.prefix}-kv"
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  tenant_id           = var.tenant_id
  deployer_object_id  = var.deployer_object_id
  tags                = local.tags
}

module "workload_identity" {
  source              = "../../modules/federated-identity"
  name                = "${local.prefix}-workload-id"
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  oidc_issuer_url     = module.aks.oidc_issuer_url
  key_vault_id        = module.keyvault.id
  tags                = local.tags
}
