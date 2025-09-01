resource "azurerm_redis_cache" "this" {
  name                          = var.name
  location                      = var.location
  resource_group_name           = var.resource_group_name
  capacity                      = var.capacity
  family                        = var.family
  sku_name                      = var.sku_name
  non_ssl_port_enabled          = false
  minimum_tls_version           = "1.2"
  public_network_access_enabled = var.public_network_access_enabled
  redis_version                 = "6"

  redis_configuration {
    # seat holds rely on keys expiring on time, never on eviction
    maxmemory_policy = "noeviction"
  }

  tags = var.tags
}
