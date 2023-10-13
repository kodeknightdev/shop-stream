terraform {
  required_version = "= 1.5.7"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "= 3.75.0"
    }
  }

  # remote state for real environments:
  # backend "azurerm" {
  #   resource_group_name  = "rg-shopstream-tfstate"
  #   storage_account_name = "stshopstreamtfstate"
  #   container_name       = "tfstate"
  #   key                  = "shop-stream.tfstate"
  # }
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
  tenant_id       = var.tenant_id
}
