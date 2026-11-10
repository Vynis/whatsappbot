-- Inicialização dos bancos de dados da Xerife Board Games
CREATE DATABASE IF NOT EXISTS `evolution_db` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE IF NOT EXISTS `xerifebot_db` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- Garantir privilégios para conexões externas e do backend
GRANT ALL PRIVILEGES ON *.* TO 'root'@'%' WITH GRANT OPTION;
FLUSH PRIVILEGES;
