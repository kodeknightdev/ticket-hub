variable "subscription_id" {
  type        = string
  description = "Target subscription. Placeholder in the example tfvars."
}

variable "tenant_id" {
  type = string
}

variable "location" {
  type    = string
  default = "uksouth"
}

variable "location_short" {
  type    = string
  default = "uks"
}

variable "deployer_object_id" {
  type        = string
  description = "Object id of the pipeline service connection's identity."
}

variable "sql_admin_group_name" {
  type = string
}

variable "sql_admin_group_object_id" {
  type = string
}

variable "aks_admin_group_object_ids" {
  type    = list(string)
  default = []
}

variable "tags" {
  type    = map(string)
  default = {}
}

variable "private_endpoint_subnet_id" {
  type        = string
  description = "Subnet for the SQL private endpoint."
  default     = null
}
