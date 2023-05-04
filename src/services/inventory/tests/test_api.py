import uuid

import pytest
from fastapi.testclient import TestClient

from app.main import create_app
from tests.conftest import make_token


@pytest.fixture()
def client(engine, product_ids, publisher, cache):
    app = create_app(start_background=False)
    app.state.publisher = publisher
    app.state.cache = cache
    with TestClient(app) as c:
        yield c


def admin_headers():
    return {"Authorization": f"Bearer {make_token(role='Admin', email='admin@shopstream.local')}"}


def test_health(client):
    assert client.get("/health").json() == {"status": "Healthy"}


def test_public_stock_level(client, product_ids):
    body = client.get(f"/api/inventory/{product_ids[1]}").json()
    assert body == {"productId": product_ids[1], "sku": "BOO-1002", "available": 2, "inStock": True, "lowStock": True}


def test_unknown_product_is_problem_details(client):
    res = client.get(f"/api/inventory/{uuid.uuid4()}")
    assert res.status_code == 404
    assert res.headers["content-type"].startswith("application/problem+json")
    assert res.json()["title"] == "Resource not found"


def test_availability_batch(client, product_ids):
    res = client.post("/api/inventory/availability", json={"productIds": product_ids})
    assert res.status_code == 200
    assert {r["sku"]: r["inStock"] for r in res.json()} == {"ELE-1001": True, "BOO-1002": True, "TOY-1003": False}


def test_list_requires_admin(client):
    assert client.get("/api/inventory").status_code == 401
    customer = {"Authorization": f"Bearer {make_token()}"}
    assert client.get("/api/inventory", headers=customer).status_code == 403


def test_admin_low_stock_filter(client):
    res = client.get("/api/inventory?lowStock=true", headers=admin_headers())
    assert res.status_code == 200
    assert sorted(i["sku"] for i in res.json()["items"]) == ["BOO-1002", "TOY-1003"]


def test_adjust_stock_validates_and_updates(client, product_ids):
    bad = client.post(f"/api/inventory/{product_ids[2]}/adjust", json={"delta": 0, "reason": "x"}, headers=admin_headers())
    assert bad.status_code == 400
    assert "delta" in bad.json()["errors"]

    ok = client.post(f"/api/inventory/{product_ids[2]}/adjust", json={"delta": 25, "reason": "restock"}, headers=admin_headers())
    assert ok.status_code == 200
    assert ok.json()["onHand"] == 25
    assert client.get(f"/api/inventory/{product_ids[2]}").json()["inStock"] is True


def test_expired_token_rejected(client):
    headers = {"Authorization": f"Bearer {make_token(role='Admin', expires_in=-120)}"}
    assert client.get("/api/inventory", headers=headers).status_code == 401
