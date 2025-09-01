output "server_id" {
  value = azurerm_mssql_server.this.id
}

output "fqdn" {
  value = azurerm_mssql_server.this.fully_qualified_domain_name
}

output "database_names" {
  value = concat(keys(azurerm_mssql_database.service), [azurerm_mssql_database.analytics.name])
}
