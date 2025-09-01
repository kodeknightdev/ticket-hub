variable "name" {
  type = string
}

variable "location" {
  type = string
}

variable "resource_group_name" {
  type = string
}

variable "entra_admin_login" {
  type = string
}

variable "entra_admin_object_id" {
  type = string
}

variable "public_network_access_enabled" {
  type    = bool
  default = false
}

variable "service_databases" {
  type        = map(string)
  description = "Database name => SKU for each transactional service."
  default = {
    TicketHub_Identity = "S0"
    TicketHub_Events   = "S0"
    TicketHub_Seating  = "S0"
    TicketHub_Orders   = "S1"
    TicketHub_Notifier = "Basic"
    TicketHub_Pricing  = "S0"
  }
}

variable "analytics_sku" {
  type    = string
  default = "S3"
}

variable "analytics_max_size_gb" {
  type    = number
  default = 100
}

variable "firewall_rules" {
  type = map(object({
    start = string
    end   = string
  }))
  default = {}
}

variable "private_endpoint_subnet_id" {
  type    = string
  default = null
}

variable "tags" {
  type    = map(string)
  default = {}
}
