variable "name" {
  type        = string
  description = "Globally unique, alphanumeric only."
}

variable "location" {
  type = string
}

variable "resource_group_name" {
  type = string
}

variable "sku" {
  type    = string
  default = "Standard"
}

variable "pull_principal_ids" {
  type        = list(string)
  description = "Principals that get AcrPull (typically the AKS kubelet identity)."
  default     = []
}

variable "tags" {
  type    = map(string)
  default = {}
}
