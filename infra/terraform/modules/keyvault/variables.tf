variable "name" {
  type = string
}

variable "location" {
  type = string
}

variable "resource_group_name" {
  type = string
}

variable "tenant_id" {
  type = string
}

variable "deployer_object_id" {
  type        = string
  description = "Object id of the pipeline identity that seeds the placeholder secrets."
}

variable "purge_protection_enabled" {
  type    = bool
  default = false
}

variable "placeholder_secrets" {
  type = map(string)
  default = {
    "stripe-secret-key"      = "sk_test_replace_me"
    "stripe-publishable-key" = "pk_test_replace_me"
    "stripe-webhook-secret"  = "whsec_replace_me"
    "jwt-signing-key"        = "replace-me-with-a-long-random-string"
    "ticket-signing-key"     = "replace-me-with-a-long-random-string"
  }
}

variable "tags" {
  type    = map(string)
  default = {}
}
