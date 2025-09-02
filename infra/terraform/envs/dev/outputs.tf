output "resource_group" {
  value = azurerm_resource_group.this.name
}

output "aks_name" {
  value = module.aks.name
}

output "acr_login_server" {
  value = module.acr.login_server
}

output "sql_fqdn" {
  value = module.mssql.fqdn
}

output "redis_hostname" {
  value = module.redis.hostname
}

output "key_vault_uri" {
  value = module.keyvault.uri
}

output "workload_identity_client_id" {
  value = module.workload_identity.client_id
}
