package battletanks

import io.gatling.core.Predef._
import io.gatling.http.Predef._
import scala.concurrent.duration._

class RankingBenchmarkSimulation extends Simulation {

  val httpProtocol = http
    .baseUrl("http://localhost:5174")
    .acceptHeader("application/json")
    .contentTypeHeader("application/json")

  val loginAndGetToken = exec(
    http("POST /api/auth/login")
      .post("/api/auth/login")
      .body(StringBody("""{"username":"testuser","password":"Test1234!"}"""))
      .check(status.in(200, 401))
      .check(jsonPath("$.token").optional.saveAs("authToken"))
  )

  val getRanking = exec(
    http("GET /api/ranking")
      .get("/api/ranking")
      .header("Authorization", "Bearer #{authToken}")
      .check(status.is(200))
  )

  val rankingScenario = scenario("Ranking Benchmark")
    .exec(loginAndGetToken)
    .pause(1.second)
    .repeat(5) {
      exec(getRanking).pause(500.milliseconds)
    }

  setUp(
    rankingScenario.inject(
      rampUsers(100).during(10.seconds),
      constantUsersPerSec(10).during(30.seconds)
    )
  ).protocols(httpProtocol)
    .assertions(
      global.responseTime.max.lt(2000),
      global.successfulRequests.percent.gt(95)
    )
}
