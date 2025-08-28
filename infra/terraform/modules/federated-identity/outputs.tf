output "client_id" {
  value       = azurerm_user_assigned_identity.workload.client_id
  description = "Goes into the Helm values as workloadIdentity.clientId."
}

output "principal_id" {
  value = azurerm_user_assigned_identity.workload.principal_id
}
