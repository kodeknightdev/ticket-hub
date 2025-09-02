terraform {
  required_version = ">= 1.11.0, < 1.14.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "= 4.37.0"
    }
  }

  # Placeholder backend; real values come from -backend-config in the pipeline.
  backend "azurerm" {
    resource_group_name  = "th-tfstate-rg"
    storage_account_name = "thtfstateplaceholder"
    container_name       = "tfstate"
    key                  = "ticket-hub.dev.tfstate"
    use_azuread_auth     = true
  }
}

provider "azurerm" {
  features {
    key_vault {
      purge_soft_delete_on_destroy = false
    }
  }
  subscription_id = var.subscription_id
}
