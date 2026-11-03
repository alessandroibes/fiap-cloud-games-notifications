# Preparação e implantação

Notifications possui Dockerfile multi-stage e Compose local conectado à infraestrutura G1. Consulte [operação](docs/operations.md) para configuração, migration explícita, execução e recuperação; [README](README.md) para o estado técnico. Compose completo pertence ao G6 e /k8s ao G7, usando Docker Desktop Kubernetes.

Não versionar .env, AGENTS.md, specs/, .codex/ ou docs/course/. Histórico Git independente, sem push ou publicação automática de imagens.
