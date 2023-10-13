output "acr_login_server" {
  description = "Goes into the shop-stream-common variable group as acrLoginServer."
  value       = azurerm_container_registry.acr.login_server
}

output "aks_name" {
  value = azurerm_kubernetes_cluster.aks.name
}

output "postgres_host" {
  description = "POSTGRES_HOST for the prod overlay ConfigMap."
  value       = azurerm_postgresql_flexible_server.pg.fqdn
}

output "redis_host" {
  description = "REDIS_HOST for the prod overlay ConfigMap."
  value       = azurerm_redis_cache.redis.hostname
}

output "log_analytics_workspace_id" {
  value = azurerm_log_analytics_workspace.logs.workspace_id
}
