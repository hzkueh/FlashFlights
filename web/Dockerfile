# The SPA ships as static files behind nginx, fronted by the gateway — the same
# shape as the .NET services: one container, one port, no build tooling in the
# runtime image.

FROM node:22-alpine AS build
WORKDIR /src

# Manifests first so a source-only change reuses the install layer.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:1.29-alpine AS final

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist /usr/share/nginx/html

EXPOSE 8080
