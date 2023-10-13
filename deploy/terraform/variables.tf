variable "subscription_id" {
  description = "Azure subscription id (placeholder by default - never commit a real one)."
  type        = string
  default     = "00000000-0000-0000-0000-000000000000"
}

variable "tenant_id" {
  description = "Azure AD tenant id (placeholder by default)."
  type        = string
  default     = "00000000-0000-0000-0000-000000000000"
}

variable "environment" {
  description = "dev or prod - matches the Kustomize overlays."
  type        = string
  default     = "dev"

  validation {
    condition     = contains(["dev", "prod"], var.environment)
    error_message = "environment must be dev or prod."
  }
}

variable "location" {
  type    = string
  default = "westeurope"
}

variable "kubernetes_version" {
  description = "AKS version (1.25-1.27 era)."
  type        = string
  default     = "1.27.3"
}

variable "node_count" {
  type    = number
  default = 2
}

variable "node_vm_size" {
  type    = string
  default = "Standard_D2s_v5"
}

variable "postgres_admin_login" {
  type    = string
  default = "shopstream"
}

variable "postgres_admin_password" {
  description = "Supplied by the pipeline from Key Vault / the variable group; there is no default on purpose."
  type        = string
  sensitive   = true
}

variable "service_databases" {
  description = "One database per service - services never share tables."
  type        = list(string)
  default     = ["identity_db", "catalog_db", "orders_db", "payments_db", "inventory_db"]
}
