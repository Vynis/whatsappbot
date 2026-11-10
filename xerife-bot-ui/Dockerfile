# =========================================================================
# Multi-stage Dockerfile para xerife-bot-ui (Angular 18 SPA)
# =========================================================================

# Stage 1: Build do Frontend com Node LTS
FROM node:20-alpine AS build
WORKDIR /app

# Copiar dependências e instalar
COPY package*.json ./
RUN npm ci || npm install

# Copiar arquivos de configuração e código fonte
COPY . .
RUN npm run build

# Stage 2: Servidor Web Nginx de alta performance
FROM nginx:alpine
WORKDIR /usr/share/nginx/html

# Limpar arquivos padrão do nginx
RUN rm -rf ./*

# Copiar build gerado pelo Angular 18
COPY --from=build /app/dist/xerife-bot-ui/browser .

# Copiar configuração customizada do Nginx com suporte a SPA
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
