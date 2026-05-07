# ============================================
# Stage 1: Build
# ============================================
FROM node:22-alpine AS build
WORKDIR /app

# Install dependencies (leveraging layer caching)
COPY package.json package-lock.json ./
RUN npm ci

# Copy source and build
COPY . .
RUN npm run build

# ============================================
# Stage 2: Serve
# ============================================
FROM nginx:alpine AS serve

# Copy custom nginx config
COPY nginx.conf /etc/nginx/conf.d/default.conf

# Copy built static files
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
