resource "azurerm_mssql_server" "this" {
  name                          = var.name
  location                      = var.location
  resource_group_name           = var.resource_group_name
  version                       = "12.0"
  minimum_tls_version           = "1.2"
  public_network_access_enabled = var.public_network_access_enabled

  # Entra ID only: services connect with workload identity, nobody holds a SQL password.
  azuread_administrator {
    login_username              = var.entra_admin_login
    object_id                   = var.entra_admin_object_id
    azuread_authentication_only = true
  }

  identity {
    type = "SystemAssigned"
  }

  tags = var.tags
}

# One database per service; they share a logical server but never each other's tables.
resource "azurerm_mssql_database" "service" {
  for_each             = var.service_databases
  name                 = each.key
  server_id            = azurerm_mssql_server.this.id
  sku_name             = each.value
  max_size_gb          = 32
  zone_redundant       = false
  storage_account_type = "Local"
  tags                 = merge(var.tags, { service = lower(replace(each.key, "TicketHub_", "")) })
}

# Analytics is read-heavy with columnstore facts, so it gets its own, larger SKU.
resource "azurerm_mssql_database" "analytics" {
  name                 = "TicketHub_Analytics"
  server_id            = azurerm_mssql_server.this.id
  sku_name             = var.analytics_sku
  max_size_gb          = var.analytics_max_size_gb
  zone_redundant       = false
  storage_account_type = "Local"
  tags                 = merge(var.tags, { service = "analytics" })
}

resource "azurerm_mssql_firewall_rule" "allowed" {
  for_each         = var.firewall_rules
  name             = each.key
  server_id        = azurerm_mssql_server.this.id
  start_ip_address = each.value.start
  end_ip_address   = each.value.end
}

resource "azurerm_private_endpoint" "sql" {
  count               = var.private_endpoint_subnet_id == null ? 0 : 1
  name                = "${var.name}-pe"
  location            = var.location
  resource_group_name = var.resource_group_name
  subnet_id           = var.private_endpoint_subnet_id

  private_service_connection {
    name                           = "${var.name}-psc"
    private_connection_resource_id = azurerm_mssql_server.this.id
    subresource_names              = ["sqlServer"]
    is_manual_connection           = false
  }

  tags = var.tags
}
