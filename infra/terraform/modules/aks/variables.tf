variable "name" {
  type = string
}

variable "location" {
  type = string
}

variable "resource_group_name" {
  type = string
}

variable "kubernetes_version" {
  type    = string
  default = null
}

variable "sku_tier" {
  type    = string
  default = "Free"
}

variable "system_vm_size" {
  type    = string
  default = "Standard_D2s_v5"
}

variable "system_node_count" {
  type    = number
  default = 1
}

variable "user_vm_size" {
  type    = string
  default = "Standard_D4s_v5"
}

variable "user_min_count" {
  type    = number
  default = 1
}

variable "user_max_count" {
  type    = number
  default = 3
}

variable "admin_group_object_ids" {
  type    = list(string)
  default = []
}

variable "log_analytics_workspace_id" {
  type = string
}

variable "tags" {
  type    = map(string)
  default = {}
}
