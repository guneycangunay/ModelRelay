.PHONY: up down test ui demo fallback stream

up:
	docker compose up --build

down:
	docker compose down

test:
	dotnet test ModelRelay.slnx -c Release

ui:
	cd ui && npm install --no-audit --no-fund && npm run build

demo:
	curl -s http://localhost:8080/v1/chat/completions \
	  -H 'Content-Type: application/json' \
	  -H 'X-Api-Key: relay_demo_key_change_me' \
	  -d '{"model":"relay-fast","messages":[{"role":"user","content":"Explain durable idempotency."}],"max_tokens":128}' | jq

fallback:
	curl -i http://localhost:8080/v1/chat/completions \
	  -H 'Content-Type: application/json' \
	  -H 'X-Api-Key: relay_demo_key_change_me' \
	  -d '{"model":"relay-fast","messages":[{"role":"user","content":"[fail-primary] explain circuit breakers"}],"max_tokens":128}'

stream:
	curl -N http://localhost:8080/v1/chat/completions \
	  -H 'Content-Type: application/json' \
	  -H 'X-Api-Key: relay_demo_key_change_me' \
	  -d '{"model":"relay-balanced","stream":true,"messages":[{"role":"user","content":"Give me three API reliability rules."}],"max_tokens":128}'
